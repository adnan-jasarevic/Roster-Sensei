using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;

namespace RosterSensei.Application.Services;

// Saves the stats the browser read from a map's demo onto a match. The demo file never reaches the
// server: the browser reads it and sends only the scoreboard, so there is nothing large to stream,
// hold in memory or clean up here.
public class DemoImportService : IDemoImportService
{
    public const string PausedMessage =
        "Demo import is paused after a CS2 update and comes back once the demo reader is updated. You can still enter stats by hand.";

    private const string EditConflictMessage =
        "Someone else changed this match while you were importing. Open it again and import the demo once more.";

    private readonly IMatchRepository _matchRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly IDemoImportRepository _demoImportRepository;
    private readonly IIdentityService _identityService;
    private readonly TimeProvider _time;

    public DemoImportService(
        IMatchRepository matchRepository,
        ITeamRepository teamRepository,
        IDemoImportRepository demoImportRepository,
        IIdentityService identityService,
        TimeProvider time)
    {
        _matchRepository = matchRepository;
        _teamRepository = teamRepository;
        _demoImportRepository = demoImportRepository;
        _identityService = identityService;
        _time = time;
    }

    public async Task<Result<DemoImportStatusDto>> GetStatusAsync(string parserVersion)
    {
        var paused = await _demoImportRepository.PausedForPatchAsync(parserVersion) != null;
        return Result<DemoImportStatusDto>.SuccessResult(new DemoImportStatusDto { Paused = paused, Message = paused ? PausedMessage : null });
    }

    public async Task<Result<DemoImportStatusDto>> ReportReadFailureAsync(string userId, int teamId, DemoReadFailureDto dto)
    {
        var team = await _teamRepository.GetByIdAsync(teamId);
        if (team == null)
            return Result<DemoImportStatusDto>.NotFound("Team was not found.");
        if (team.Members.FirstOrDefault(m => m.UserId == userId) is not { CanManageTeam: true })
            return Result<DemoImportStatusDto>.Forbidden("Only the team Owner, Captain, Officers or Coaches can import demos.");

        // A demo that fails on a patch we have already imported from is just a bad file. One that fails
        // on a newer patch most likely means a CS2 update changed the demo format, so stop everyone
        // from trying until a new parser version ships.
        if (dto.PatchVersion.HasValue)
        {
            var newest = await _demoImportRepository.NewestImportedPatchAsync();
            if (newest.HasValue && dto.PatchVersion.Value > newest.Value)
                await _demoImportRepository.PauseAsync(dto.ParserVersion, dto.PatchVersion.Value, _time.GetUtcNow().UtcDateTime);
        }

        return await GetStatusAsync(dto.ParserVersion);
    }

    public async Task<Result<DemoImportPreviewDto>> PreviewAsync(string userId, int teamId, int matchId, PreviewDemoImportDto dto)
    {
        var demo = dto.Demo;
        var (team, match, error) = await LoadAsync<DemoImportPreviewDto>(userId, teamId, matchId, demo);
        if (error != null)
            return error;

        var duplicate = await DuplicateMessageAsync(teamId, match!, demo.Fingerprint, mapOrder: null);
        if (duplicate != null)
            return Result<DemoImportPreviewDto>.Failure(duplicate);

        var links = await LinkAsync(team!, demo);
        var mapName = NormalizeMapName(demo.MapName);
        var mapOrder = SuggestMapOrder(match!, mapName, demo.Fingerprint);

        var sides = demo.Sides.Select(side => new DemoSidePreviewDto
        {
            Score = side.Score,
            FirstHalf = side.FirstHalf,
            SecondHalf = side.SecondHalf,
            Players = side.Players.Select(p =>
            {
                links.TryGetValue(p.SteamId, out var link);
                return new DemoPlayerPreviewDto
                {
                    SteamId = p.SteamId,
                    Name = p.Name,
                    UserId = link?.UserId,
                    Username = link?.Username,
                    CanPlay = link?.CanPlay ?? true,
                    Kills = p.Kills,
                    Deaths = p.Deaths,
                    Assists = p.Assists,
                    Adr = Adr(p.Damage, demo.Rounds),
                    HeadshotPercentage = HeadshotPercentage(p.HeadshotKills, p.Kills)
                };
            }).ToList(),
            Coaches = side.Coaches.Select(c =>
            {
                links.TryGetValue(c.SteamId, out var link);
                return new DemoCoachPreviewDto { SteamId = c.SteamId, Name = c.Name, UserId = link?.UserId, Username = link?.Username };
            }).ToList()
        }).ToList();

        var memberCounts = demo.Sides.Select(s => s.Players.Count(p => links.ContainsKey(p.SteamId))).ToList();
        int? ourSide = memberCounts.Max() == 0 ? null : memberCounts[0] >= memberCounts[1] ? 0 : 1;

        var coaches = ourSide.HasValue ? MergeCoaches(match!.Coaches, demo.Sides[ourSide.Value].Coaches, links) : match!.Coaches.ToList();
        var usernames = await _identityService.GetUsernamesByIdsAsync(coaches.Where(c => c.UserId != null).Select(c => c.UserId!));

        return Result<DemoImportPreviewDto>.SuccessResult(new DemoImportPreviewDto
        {
            OurSide = ourSide,
            MapOrder = mapOrder,
            ReplacesImport = match.DemoImports.Any(i => i.MapOrder == mapOrder),
            Sides = sides,
            Coaches = MatchCoachList.ToDto(coaches, usernames)
        });
    }

    public async Task<Result<MatchDto>> ApplyAsync(string userId, int teamId, int matchId, ApplyDemoImportDto dto)
    {
        var demo = dto.Demo;
        var (team, match, error) = await LoadAsync<MatchDto>(userId, teamId, matchId, demo);
        if (error != null)
            return error;
        if (match!.Version != dto.Version)
            return Result<MatchDto>.Conflict(EditConflictMessage);

        var duplicate = await DuplicateMessageAsync(teamId, match, demo.Fingerprint, dto.MapOrder);
        if (duplicate != null)
            return Result<MatchDto>.Failure(duplicate);

        // Which map of the match this demo fills.
        var mapName = NormalizeMapName(demo.MapName);
        var maxMaps = match.BestOf ?? Math.Max(1, match.Maps.Count);
        if (dto.MapOrder > Math.Min(match.Maps.Count + 1, maxMaps))
            return Result<MatchDto>.Failure(match.Maps.Count >= maxMaps
                ? $"This match has no map {dto.MapOrder}. Pick one of its {match.Maps.Count} maps."
                : $"Maps are filled in order: this demo can be map {match.Maps.Count + 1} at most.");
        var map = match.Maps.FirstOrDefault(m => m.Order == dto.MapOrder);
        if (map != null && !string.IsNullOrWhiteSpace(map.MapName) && !string.Equals(map.MapName, mapName, StringComparison.OrdinalIgnoreCase))
            return Result<MatchDto>.Failure($"Map {dto.MapOrder} of this match is {map.MapName}, but the demo was played on {mapName}.");

        var ours = demo.Sides[dto.OurSide];
        var theirs = demo.Sides[1 - dto.OurSide];
        var mapScore = new MatchMapDto
        {
            MapName = mapName,
            OurScore = ours.Score,
            OpponentScore = theirs.Score,
            OurFirstHalf = ours.FirstHalf,
            OpponentFirstHalf = theirs.FirstHalf,
            OurSecondHalf = ours.SecondHalf,
            OpponentSecondHalf = theirs.SecondHalf
        };
        var mapCheck = new MatchMapDtoValidator().Validate(mapScore);
        if (!mapCheck.IsValid)
            return Result<MatchDto>.Failure(mapCheck.Errors[0].ErrorMessage);

        // Who each of our players is.
        var links = await LinkAsync(team!, demo);
        var players = new List<(DemoPlayerDto Player, string UserId)>();
        if (dto.Players == null)
        {
            foreach (var player in ours.Players)
                if (links.TryGetValue(player.SteamId, out var link) && link.CanPlay)
                    players.Add((player, link.UserId));
        }
        else
        {
            var inLineup = match.Participations.Select(p => p.UserId).ToHashSet();
            foreach (var chosen in dto.Players.Where(l => !string.IsNullOrWhiteSpace(l.UserId)))
            {
                var player = ours.Players.FirstOrDefault(p => p.SteamId == chosen.SteamId);
                if (player == null)
                    return Result<MatchDto>.Failure("Only players from our side of the demo can be linked.");

                var member = team!.Members.FirstOrDefault(m => m.UserId == chosen.UserId);
                if (member == null && !inLineup.Contains(chosen.UserId!))
                    return Result<MatchDto>.Failure("Every linked player must be a member of the team.");
                if (member != null && !member.Role.CanPlay() && !inLineup.Contains(chosen.UserId!))
                    return Result<MatchDto>.Failure("Coaches can't be put in a lineup.");

                players.Add((player, chosen.UserId!));
            }
        }
        if (players.Count == 0)
            return Result<MatchDto>.Failure("Nobody on our side of the demo is linked to a team member, so there are no stats to save.");

        List<MatchCoach>? coaches;
        if (dto.Coaches != null)
        {
            coaches = MatchCoachList.Build(dto.Coaches, team!, match.Coaches);
            if (coaches == null)
                return Result<MatchDto>.Failure(MatchCoachList.NotTeamMemberMessage);
        }
        else
        {
            coaches = MergeCoaches(match.Coaches, ours.Coaches, links)
                .Select(c => new MatchCoach { UserId = c.UserId, Name = c.Name, SteamId = c.SteamId })
                .ToList();
        }

        // The map's score, from the demo.
        if (map == null)
        {
            map = new MatchMap { Order = dto.MapOrder, MapName = mapName };
            match.Maps.Add(map);
        }
        map.MapName = mapName;
        map.OurScore = mapScore.OurScore;
        map.OpponentScore = mapScore.OpponentScore;
        map.OurFirstHalf = mapScore.OurFirstHalf;
        map.OpponentFirstHalf = mapScore.OpponentFirstHalf;
        map.OurSecondHalf = mapScore.OurSecondHalf;
        map.OpponentSecondHalf = mapScore.OpponentSecondHalf;

        // A new demo for a map replaces the one imported before.
        var affected = new HashSet<string>(players.Select(p => p.UserId));
        foreach (var old in match.DemoImports.Where(i => i.MapOrder == dto.MapOrder).ToList())
        {
            affected.UnionWith(old.Players.Select(p => p.UserId));
            match.DemoImports.Remove(old);
        }

        match.DemoImports.Add(new MatchDemoImport
        {
            TeamId = teamId,
            MapOrder = dto.MapOrder,
            MapName = mapName,
            Fingerprint = demo.Fingerprint,
            PatchVersion = demo.PatchVersion,
            ParserVersion = demo.ParserVersion,
            Rounds = demo.Rounds,
            ImportedByUserId = userId,
            ImportedAt = _time.GetUtcNow().UtcDateTime,
            Players = players.Select(p => new DemoPlayerStats
            {
                UserId = p.UserId,
                SteamId = p.Player.SteamId,
                Kills = p.Player.Kills,
                Deaths = p.Player.Deaths,
                Assists = p.Player.Assists,
                Damage = p.Player.Damage,
                HeadshotKills = p.Player.HeadshotKills
            }).ToList()
        });

        // Lineup totals are the sums over every imported map. They replace stats entered by hand;
        // a rating stays, since demos don't have one.
        foreach (var playerId in affected)
        {
            var rows = match.DemoImports
                .SelectMany(i => i.Players.Where(p => p.UserId == playerId).Select(p => (Stats: p, i.Rounds)))
                .ToList();
            var participation = match.Participations.FirstOrDefault(p => p.UserId == playerId);
            if (rows.Count == 0)
            {
                if (participation != null)
                {
                    var rating = participation.Rating;
                    participation.ClearStats();
                    participation.Rating = rating;
                }
                continue;
            }

            if (participation == null)
            {
                participation = new MatchParticipation { UserId = playerId };
                match.Participations.Add(participation);
            }
            int kills = rows.Sum(r => r.Stats.Kills);
            participation.Played = true;
            participation.Kills = Math.Min(kills, PlayerMatchStats.MaxCount);
            participation.Deaths = Math.Min(rows.Sum(r => r.Stats.Deaths), PlayerMatchStats.MaxCount);
            participation.Assists = Math.Min(rows.Sum(r => r.Stats.Assists), PlayerMatchStats.MaxCount);
            participation.Adr = Adr(rows.Sum(r => r.Stats.Damage), rows.Sum(r => r.Rounds));
            participation.HeadshotPercentage = HeadshotPercentage(rows.Sum(r => r.Stats.HeadshotKills), kills);
        }

        match.Coaches.Clear();
        foreach (var coach in coaches)
            match.Coaches.Add(coach);

        // A demo means the map was played.
        match.Status = MatchStatus.Finished;
        if (MatchScores.FromMaps(match.Maps, match.BestOf) is var (our, opponent))
        {
            match.OurScore = our;
            match.OpponentScore = opponent;
        }

        if (!await _matchRepository.UpdateAsync(match, dto.Version))
        {
            // Lost a race: the match was removed, edited, or the same demo was imported at the same time.
            if (await _matchRepository.GetForEditAsync(teamId, matchId) == null)
                return Result<MatchDto>.NotFound("Match was not found. It may have just been removed.");
            var again = await DuplicateMessageAsync(teamId, match, demo.Fingerprint, dto.MapOrder, ignoreLoaded: true);
            return again != null ? Result<MatchDto>.Failure(again) : Result<MatchDto>.Conflict(EditConflictMessage);
        }

        var usernames = await _identityService.GetUsernamesByIdsAsync(MatchService.UserIdsOf(match));
        return Result<MatchDto>.SuccessResult(MatchService.MapToDto(match, usernames));
    }

    private sealed record Link(string UserId, string Username, bool CanPlay);

    private async Task<(Team? Team, Match? Match, Result<T>? Error)> LoadAsync<T>(string userId, int teamId, int matchId, DemoSummaryDto demo)
    {
        var team = await _teamRepository.GetByIdAsync(teamId);
        if (team == null)
            return (null, null, Result<T>.NotFound("Team was not found."));
        if (team.Members.FirstOrDefault(m => m.UserId == userId) is not { CanManageTeam: true })
            return (null, null, Result<T>.Forbidden("Only the team Owner, Captain, Officers or Coaches can import demos."));

        if (await _demoImportRepository.PausedForPatchAsync(demo.ParserVersion) != null)
            return (null, null, Result<T>.Failure(PausedMessage));

        var match = await _matchRepository.GetForEditAsync(teamId, matchId);
        if (match == null)
            return (null, null, Result<T>.NotFound("Match was not found."));
        if (match.Status == MatchStatus.Cancelled)
            return (null, null, Result<T>.Failure("A cancelled match can't have a demo. Change its status first."));

        return (team, match, null);
    }

    // Demo players who are members of this team, by Steam ID. One query for all of them.
    private async Task<Dictionary<string, Link>> LinkAsync(Team team, DemoSummaryDto demo)
    {
        var steamIds = demo.Sides.SelectMany(s => s.Players.Select(p => p.SteamId).Concat(s.Coaches.Select(c => c.SteamId)));
        var byUser = await _identityService.GetUserIdsBySteamIdsAsync(steamIds);
        var members = byUser
            .Select(pair => (SteamId: pair.Key, Member: team.Members.FirstOrDefault(m => m.UserId == pair.Value)))
            .Where(x => x.Member != null)
            .ToList();
        var usernames = await _identityService.GetUsernamesByIdsAsync(members.Select(x => x.Member!.UserId));
        return members.ToDictionary(
            x => x.SteamId,
            x => new Link(x.Member!.UserId, usernames.GetValueOrDefault(x.Member.UserId) ?? "Unknown", x.Member.Role.CanPlay()));
    }

    // The saved coaches plus our side's coaches from the demo, without listing anyone twice.
    private static List<MatchCoach> MergeCoaches(IEnumerable<MatchCoach> saved, IEnumerable<DemoCoachDto> fromDemo, IReadOnlyDictionary<string, Link> links)
    {
        var result = saved.Select(c => new MatchCoach { Id = c.Id, UserId = c.UserId, Name = c.Name, SteamId = c.SteamId }).ToList();
        foreach (var coach in fromDemo)
        {
            links.TryGetValue(coach.SteamId, out var link);
            if (result.Any(c => c.SteamId == coach.SteamId || (link != null && c.UserId == link.UserId)))
                continue;
            result.Add(new MatchCoach
            {
                UserId = link?.UserId,
                Name = string.IsNullOrWhiteSpace(coach.Name) ? null : coach.Name.Trim(),
                SteamId = coach.SteamId
            });
        }
        return result;
    }

    // A message when this team already imported the demo somewhere other than this map of this match.
    private async Task<string?> DuplicateMessageAsync(int teamId, Match match, string fingerprint, int? mapOrder, bool ignoreLoaded = false)
    {
        var found = await _demoImportRepository.FindImportAsync(teamId, fingerprint);
        if (found == null)
            return null;
        var (foundMatch, foundOrder, opponent) = found.Value;
        if (foundMatch == match.Id && (mapOrder == null || foundOrder == mapOrder) && !ignoreLoaded)
            return null;
        return foundMatch == match.Id
            ? $"This demo was already imported as map {foundOrder} of this match."
            : $"This demo was already imported for the match against {opponent}.";
    }

    private static int SuggestMapOrder(Match match, string mapName, string fingerprint)
    {
        var sameDemo = match.DemoImports.FirstOrDefault(i => i.Fingerprint == fingerprint);
        if (sameDemo != null)
            return sameDemo.MapOrder;

        var imported = match.DemoImports.Select(i => i.MapOrder).ToHashSet();
        var sameMap = match.Maps
            .OrderBy(m => m.Order)
            .FirstOrDefault(m => !imported.Contains(m.Order) && string.Equals(m.MapName, mapName, StringComparison.OrdinalIgnoreCase));
        if (sameMap != null)
            return sameMap.Order;

        return match.Maps.Count == 0 ? 1 : match.Maps.Max(m => m.Order) + 1;
    }

    // "de_mirage" becomes "Mirage", as the map pool spells it. A map outside the pool keeps its name.
    public static string NormalizeMapName(string mapName)
    {
        var name = mapName.Trim();
        foreach (var prefix in new[] { "de_", "cs_", "ar_" })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                name = name[prefix.Length..];
        return MapPool.Find(name) ?? (name.Length == 0 ? mapName.Trim() : char.ToUpperInvariant(name[0]) + name[1..]);
    }

    public static decimal Adr(int damage, int rounds) =>
        rounds == 0 ? 0m : Math.Min(Math.Round((decimal)damage / rounds, 1, MidpointRounding.AwayFromZero), PlayerMatchStats.MaxAdr);

    public static decimal HeadshotPercentage(int headshotKills, int kills) =>
        kills == 0 ? 0m : Math.Round(100m * headshotKills / kills, 1, MidpointRounding.AwayFromZero);
}
