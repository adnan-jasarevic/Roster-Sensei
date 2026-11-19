using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Infrastructure.Identity;
using RosterSensei.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace RosterSensei.Infrastructure.Services;

// Generates made-up but realistic team data for the master tester.
//
// Who counts as a master tester lives in configuration, never in code:
//   MasterTester:Enabled   true only in Development (appsettings.Development.json)
//   MasterTester:Emails    the tester accounts, set with user-secrets
// The controller also refuses everything in Production, whatever the configuration says.
//
// It writes straight to the database instead of going through the services: test data may break
// everyday limits (like owning at most 3 teams), but it always passes the same validation as data
// entered by hand, so every screen shows it the way it would show real data.
public class MasterTesterService : IMasterTesterService
{
    private const string SeedEmailDomain = "rostersensei.test";
    private const int MaxRosterSize = 12;
    private const int PlayersAddedPerClick = 6;

    private static readonly (string Name, string Role)[] SeedPlayers =
    [
        ("kaiser", "IGL"), ("nyx", "AWPer"), ("sable", "Entry"), ("rook", "Support"),
        ("volt", "Lurker"), ("ember", "Rifler"), ("drift", "Rifler"), ("hollow", "Support"),
        ("quill", "AWPer"), ("mako", "Entry"), ("onyx", "Lurker"), ("pixel", "Rifler"),
    ];

    private static readonly string[] Opponents =
    [
        "Northwind Five", "Ashfall", "Kestrel Esports", "Red Lantern", "Glass Cannon", "Ironbark",
        "Paper Tigers", "Low Orbit", "Copper Foxes", "Midnight Static", "Saltwater", "Bluefin Gaming",
        "Hexgate", "Old Mill", "Quiet Storm", "Lucky Seven",
    ];

    private static readonly string?[] Tournaments =
    [
        "Community Cup #14", "Thursday Night Open", "Regional League Div. 3", "Winter Open Qualifier", null,
    ];

    private static readonly string[] MatchNotes =
    [
        "Lost both pistol rounds, won anyway.",
        "Great comeback in the second half.",
        "They stacked B on almost every CT round.",
        "Our T-side defaults need work.",
        "Clean anti-ecos, sloppy retakes.",
    ];

    private static readonly string[] TeamAdjectives = ["Crimson", "Silent", "Paper", "Iron", "Neon", "Hidden", "Northern", "Midnight"];
    private static readonly string[] TeamNouns = ["Foxes", "Ronin", "Lanterns", "Herons", "Tigers", "Owls", "Comets", "Oni"];

    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly Random _random = new();

    public MasterTesterService(AppDbContext db, UserManager<AppUser> userManager, IConfiguration configuration)
    {
        _db = db;
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task<bool> IsMasterTesterAsync(string userId)
    {
        if (!_configuration.GetValue<bool>("MasterTester:Enabled"))
            return false;

        var emails = _configuration.GetSection("MasterTester:Emails").Get<string[]>() ?? [];
        if (emails.Length == 0)
            return false;

        // Read from the database, not the token, so the check follows the account's current email.
        var user = await _userManager.FindByIdAsync(userId);
        return user?.Email != null && emails.Any(e => string.Equals(e.Trim(), user.Email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Result<SeedResultDto>> CreateFullTeamAsync(string userId, SeedTeamDto dto)
    {
        var timeZone = ResolveTimeZone(dto.TimeZone);
        var name = string.IsNullOrWhiteSpace(dto.Name)
            ? $"{Pick(TeamAdjectives)} {Pick(TeamNouns)}"
            : dto.Name.Trim();
        // The same rules as CreateTeamDtoValidator, so a test team never has a name a real one couldn't.
        var nameCheck = new CreateTeamDtoValidator().Validate(new CreateTeamDto { Name = name });
        if (!nameCheck.IsValid)
            return Result<SeedResultDto>.Failure(nameCheck.Errors[0].ErrorMessage);

        var team = NewTeam(name, userId);
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();

        await AddRosterAsync(team.Id, replace: false);
        await AddMatchesAsync(team.Id, timeZone, replace: false);
        await AddCalendarAsync(team.Id, userId, timeZone, replace: false);
        await AddOffDaysAsync(team.Id, userId, timeZone, replace: false);
        await AddMapPoolAsync(team.Id, replace: false);

        return Result<SeedResultDto>.SuccessResult(new SeedResultDto
        {
            TeamId = team.Id,
            Message = $"Created \"{team.Name}\" with a full roster, matches, calendar, off days and map pool."
        });
    }

    public async Task<Result<SeedResultDto>> FillSectionAsync(string userId, int teamId, string section, SeedSectionDto dto)
    {
        if (!TryReadMode(dto.Mode, out var replace))
            return Result<SeedResultDto>.Failure("Mode must be \"add\" or \"replace\".");

        if (!await _db.Teams.AnyAsync(t => t.Id == teamId))
            return Result<SeedResultDto>.NotFound("Team was not found.");
        if (!await _db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId))
            return Result<SeedResultDto>.Forbidden("You are not in this team.");

        var timeZone = ResolveTimeZone(dto.TimeZone);
        string message;
        switch (section.ToLowerInvariant())
        {
            case MasterTesterSections.Roster:
                message = await AddRosterAsync(teamId, replace);
                break;
            case MasterTesterSections.Matches:
                message = await AddMatchesAsync(teamId, timeZone, replace);
                break;
            case MasterTesterSections.Calendar:
                message = await AddCalendarAsync(teamId, userId, timeZone, replace);
                break;
            case MasterTesterSections.OffDays:
                message = await AddOffDaysAsync(teamId, userId, timeZone, replace);
                break;
            case MasterTesterSections.MapPool:
                message = await AddMapPoolAsync(teamId, replace);
                break;
            default:
                return Result<SeedResultDto>.NotFound($"Unknown section \"{section}\".");
        }

        return Result<SeedResultDto>.SuccessResult(new SeedResultDto { TeamId = teamId, Message = message });
    }

    public async Task<Result<SeedResultDto>> SendInvitesAsync(string userId, SeedSectionDto dto)
    {
        if (!TryReadMode(dto.Mode, out var replace))
            return Result<SeedResultDto>.Failure("Mode must be \"add\" or \"replace\".");

        var tester = await _userManager.FindByIdAsync(userId);
        if (tester?.Email == null)
            return Result<SeedResultDto>.NotFound("Your account was not found.");

        if (replace)
        {
            await _db.TeamInvites
                .Where(i => i.TargetUserId == userId && i.Status == InviteStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, InviteStatus.Canceled)
                    .SetProperty(i => i.RespondedAt, DateTime.UtcNow));
        }

        // Each invite comes from a fresh team owned by a made-up player, so there's never a duplicate.
        var players = await EnsureSeedPlayersAsync();
        foreach (var owner in players.OrderBy(_ => _random.Next()).Take(2))
        {
            var team = NewTeam($"{Pick(TeamAdjectives)} {Pick(TeamNouns)}", owner.Id);
            team.Invites.Add(new TeamInvite
            {
                TargetUserEmail = tester.Email,
                TargetUserId = userId,
                SenderUserId = owner.Id,
                Status = InviteStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });
            _db.Teams.Add(team);
        }
        await _db.SaveChangesAsync();

        return Result<SeedResultDto>.SuccessResult(new SeedResultDto
        {
            Message = replace ? "Replaced your pending invites with 2 new ones." : "Sent you 2 invites. Check the bell."
        });
    }

    // ---- Sections ----

    private async Task<string> AddRosterAsync(int teamId, bool replace)
    {
        var team = await _db.Teams.Include(t => t.Members).SingleAsync(t => t.Id == teamId);

        int removed = 0;
        if (replace)
        {
            // The Owner stays, so the team keeps working; everyone else goes (their map ratings with them).
            var toRemove = team.Members.Where(m => m.UserId != team.OwnerId).ToList();
            removed = toRemove.Count;
            _db.TeamMembers.RemoveRange(toRemove);
            await _db.SaveChangesAsync();
        }

        var players = await EnsureSeedPlayersAsync();
        var current = team.Members.Where(m => _db.Entry(m).State != EntityState.Detached).Select(m => m.UserId).ToHashSet();
        var room = Math.Max(0, MaxRosterSize - current.Count);
        var toAdd = players.Where(p => !current.Contains(p.Id)).Take(Math.Min(PlayersAddedPerClick, room)).ToList();

        bool hasOfficer = team.Members.Any(m => current.Contains(m.UserId) && m.Role == TeamRole.Officer);
        foreach (var player in toAdd)
        {
            _db.TeamMembers.Add(new TeamMember
            {
                TeamId = teamId,
                UserId = player.Id,
                Role = hasOfficer ? TeamRole.Player : TeamRole.Officer,
                JoinedAt = DateTime.UtcNow.AddDays(-_random.Next(5, 200))
            });
            hasOfficer = true;
        }
        await _db.SaveChangesAsync();

        if (toAdd.Count == 0)
            return $"The roster is full ({MaxRosterSize} players), so nobody was added.";
        return replace
            ? $"Removed {removed} and added {toAdd.Count} players."
            : $"Added {toAdd.Count} players.";
    }

    private async Task<string> AddMatchesAsync(int teamId, TimeZoneInfo timeZone, bool replace)
    {
        if (replace)
        {
            var existing = await _db.Matches.Where(m => m.TeamId == teamId)
                .Include(m => m.Maps).Include(m => m.Participations).Include(m => m.TeamStats)
                .ToListAsync();
            _db.Matches.RemoveRange(existing);
            await _db.SaveChangesAsync();
        }

        var lineup = await LineupPoolAsync(teamId);
        var strengths = MapStrengths();
        var today = LocalToday(timeZone);
        var matches = new List<Match>();

        // 14 results over the last ~100 days.
        foreach (var daysAgo in Enumerable.Range(0, 14).Select(_ => _random.Next(3, 100)).Order())
        {
            var start = ToUtc(today.AddDays(-daysAgo), new TimeOnly(19 + _random.Next(0, 3), 0), timeZone);
            matches.Add(FinishedMatch(teamId, start, strengths, lineup));
        }

        // One that was scheduled two days ago but has no result yet: shows as "Needs result".
        matches.Add(ScheduledMatch(teamId, ToUtc(today.AddDays(-2), new TimeOnly(20, 0), timeZone)));

        // Four coming up over the next three weeks.
        foreach (var daysAhead in Enumerable.Range(0, 4).Select(_ => _random.Next(1, 22)).Order())
            matches.Add(ScheduledMatch(teamId, ToUtc(today.AddDays(daysAhead), new TimeOnly(19 + _random.Next(0, 3), 0), timeZone)));

        _db.Matches.AddRange(matches);
        await _db.SaveChangesAsync();

        return replace
            ? "Replaced all matches with 14 results and 5 scheduled matches."
            : "Added 14 results and 5 scheduled matches.";
    }

    private async Task<string> AddCalendarAsync(int teamId, string userId, TimeZoneInfo timeZone, bool replace)
    {
        if (replace)
            await _db.TeamEvents.Where(e => e.TeamId == teamId).ExecuteDeleteAsync();

        var types = await EnsureEventTypesAsync(teamId);
        var (first, last) = CalendarWindow(timeZone);
        var offDays = (await _db.TeamOffDays.Where(d => d.TeamId == teamId).Select(d => d.Date).ToListAsync()).ToHashSet();

        var events = new List<TeamEvent>();
        void Add(DateOnly day, EventTypePreset preset, string title, TimeOnly start, int minutes, string? description = null)
        {
            var startUtc = ToUtc(day, start, timeZone);
            events.Add(new TeamEvent
            {
                TeamId = teamId,
                EventTypeId = types[preset],
                Title = title,
                Description = description,
                StartTime = startUtc,
                EndTime = startUtc.AddMinutes(minutes),
                CreatedByUserId = userId
            });
        }

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            // New events can't go on an off day; the app refuses them too.
            if (offDays.Contains(day)) continue;

            switch (day.DayOfWeek)
            {
                case DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Thursday when Chance(0.75):
                    Add(day, EventTypePreset.Pracc, $"Pracc vs {Pick(Opponents)}", new TimeOnly(19, 0), 180, Chance(0.3) ? $"Focus on {Pick(MapPool.Maps)} retakes." : null);
                    break;
                case DayOfWeek.Wednesday when Chance(0.7):
                    Add(day, EventTypePreset.Theory, $"Theory: {Pick(MapPool.Maps)} {(Chance(0.5) ? "T" : "CT")} setups", new TimeOnly(18, 30), 60);
                    break;
                case DayOfWeek.Friday when Chance(0.6):
                    Add(day, EventTypePreset.Scrim, $"Scrim vs {Pick(Opponents)}", new TimeOnly(20, 0), 120);
                    break;
                case DayOfWeek.Saturday when Chance(0.35):
                    Add(day, EventTypePreset.OfficialMatch, $"{Pick(Tournaments) ?? "Cup match"} vs {Pick(Opponents)}", new TimeOnly(17, 0), 120);
                    break;
                case DayOfWeek.Sunday when Chance(0.7):
                    Add(day, EventTypePreset.VodReview, $"VOD review: {Pick(Opponents)}", new TimeOnly(18, 0), 90);
                    break;
            }

            if (Chance(0.08))
                Add(day, EventTypePreset.Other, "Team meeting", new TimeOnly(17, 0), 45, "Roles, schedule and goals for the next weeks.");
        }

        _db.TeamEvents.AddRange(events);
        await _db.SaveChangesAsync();

        return $"{(replace ? "Replaced the calendar with" : "Added")} {events.Count} events from {first:MMM d} to {last:MMM d}.";
    }

    private async Task<string> AddOffDaysAsync(int teamId, string userId, TimeZoneInfo timeZone, bool replace)
    {
        if (replace)
            await _db.TeamOffDays.Where(d => d.TeamId == teamId).ExecuteDeleteAsync();

        var (first, last) = CalendarWindow(timeZone);
        var taken = (await _db.TeamOffDays.Where(d => d.TeamId == teamId).Select(d => d.Date).ToListAsync()).ToHashSet();
        var span = last.DayNumber - first.DayNumber + 1;

        // About two per month. Some land on days with events, which then show as cancelled.
        var added = new List<DateOnly>();
        for (int attempt = 0; added.Count < 6 && attempt < 100; attempt++)
        {
            var day = first.AddDays(_random.Next(span));
            if (!taken.Add(day)) continue;
            added.Add(day);
            _db.TeamOffDays.Add(new TeamOffDay
            {
                TeamId = teamId,
                Date = day,
                StartTime = ToUtc(day, TimeOnly.MinValue, timeZone),
                EndTime = ToUtc(day.AddDays(1), TimeOnly.MinValue, timeZone),
                CreatedByUserId = userId
            });
        }
        await _db.SaveChangesAsync();

        return added.Count == 0
            ? "Every day is already off, so nothing was added."
            : $"{(replace ? "Replaced off days with" : "Marked")} {added.Count} off days: {string.Join(", ", added.Order().Select(d => d.ToString("MMM d")))}.";
    }

    private async Task<string> AddMapPoolAsync(int teamId, bool replace)
    {
        if (replace)
            await _db.MapRatings.Where(r => r.TeamId == teamId).ExecuteDeleteAsync();

        var members = await _db.TeamMembers.Where(m => m.TeamId == teamId).Select(m => m.UserId).ToListAsync();
        var existing = (await _db.MapRatings.Where(r => r.TeamId == teamId)
                .Select(r => new { r.UserId, r.MapName, r.Side }).ToListAsync())
            .Select(r => (r.UserId, r.MapName, r.Side)).ToHashSet();

        // The whole team agrees roughly on which maps are good; each player adds their own opinion.
        var strengths = MapStrengths();
        int added = 0;
        foreach (var userId in members)
        foreach (var map in MapPool.Maps)
        foreach (var side in new[] { MapSide.T, MapSide.CT })
        {
            if (existing.Contains((userId, map, side))) continue;
            var sideLean = side == MapSide.CT ? 0.4 : -0.2;
            var value = 5.5 + strengths[map] * 12 + sideLean + (_random.NextDouble() * 3 - 1.5);
            _db.MapRatings.Add(new MapRating
            {
                TeamId = teamId,
                UserId = userId,
                MapName = map,
                Side = side,
                Rating = Math.Clamp(Math.Round((decimal)value, 1), MapPool.MinRating, MapPool.MaxRating),
                UpdatedAt = DateTime.UtcNow.AddDays(-_random.Next(0, 30))
            });
            added++;
        }
        await _db.SaveChangesAsync();

        return added == 0
            ? "Every member has already rated every map."
            : $"{(replace ? "Replaced the map pool with" : "Added")} {added} ratings for {members.Count} members.";
    }

    // ---- Matches ----

    private Match FinishedMatch(int teamId, DateTime start, Dictionary<string, double> strengths, List<(string UserId, string Role)> lineupPool)
    {
        var bestOf = PickWeighted((1, 4), (3, 5), (2, 1), (5, 1));
        var isOfficial = Chance(0.55);
        var maps = new List<MatchMap>();
        var pool = MapPool.Maps.OrderBy(_ => _random.Next()).ToList();

        int needed = bestOf == 2 ? 2 : bestOf / 2 + 1;
        int ourMaps = 0, theirMaps = 0;
        while (bestOf == 2 ? maps.Count < 2 : ourMaps < needed && theirMaps < needed)
        {
            var mapName = pool[maps.Count];
            var weWin = Chance(0.5 + strengths[mapName]);
            if (weWin) ourMaps++; else theirMaps++;
            maps.Add(PlayedMap(maps.Count + 1, mapName, weWin));
        }

        var (ourScore, theirScore) = bestOf == 1
            ? (maps[0].OurScore!.Value, maps[0].OpponentScore!.Value)
            : (ourMaps, theirMaps);

        var match = new Match
        {
            TeamId = teamId,
            OpponentName = Pick(Opponents),
            Tournament = isOfficial ? Pick(Tournaments) : null,
            ScheduledDate = start,
            PlayedDate = start,
            Status = MatchStatus.Finished,
            Type = isOfficial ? MatchKind.Official : MatchKind.Scrim,
            BestOf = bestOf,
            OurScore = ourScore,
            OpponentScore = theirScore,
            Maps = maps,
            Notes = Chance(0.3) ? Pick(MatchNotes) : null
        };

        // Five who played, plus one on the bench when the roster has a sixth player. Players who played
        // get an HLTV-style rating (around 1.00), a bit higher after a win; now and then one is left unrated.
        bool won = ourScore > theirScore;
        foreach (var (player, index) in lineupPool.Take(6).Select((p, i) => (p, i)))
        {
            bool played = index < 5;
            decimal? rating = played && Chance(0.92)
                ? Math.Clamp(Math.Round((decimal)(0.55 + _random.NextDouble() * 0.85 + (won ? 0.15 : 0)), 2), PlayerRating.Min, PlayerRating.Max)
                : null;
            match.Participations.Add(new MatchParticipation { UserId = player.UserId, Role = player.Role, Played = played, Rating = rating });
        }

        return match;
    }

    private Match ScheduledMatch(int teamId, DateTime start)
    {
        var bestOf = PickWeighted((1, 2), (3, 3));
        var isOfficial = Chance(0.6);
        var maps = Chance(0.5)
            ? MapPool.Maps.OrderBy(_ => _random.Next()).Take(bestOf)
                .Select((name, i) => new MatchMap { Order = i + 1, MapName = name }).ToList()
            : new List<MatchMap>();

        return new Match
        {
            TeamId = teamId,
            OpponentName = Pick(Opponents),
            Tournament = isOfficial ? Pick(Tournaments) : null,
            ScheduledDate = start,
            Status = MatchStatus.Scheduled,
            Type = isOfficial ? MatchKind.Official : MatchKind.Scrim,
            BestOf = bestOf,
            Maps = maps
        };
    }

    // A map score with half scores that pass MatchMapDtoValidator: MR12, and overtime only after 12-12.
    private MatchMap PlayedMap(int order, string mapName, bool weWin)
    {
        int winnerFirst, loserFirst, winnerSecond, loserSecond, winnerTotal, loserTotal;

        if (Chance(0.12))
        {
            // 12-12 after regulation, then overtime.
            winnerFirst = _random.Next(3, 10);
            loserFirst = 12 - winnerFirst;
            winnerSecond = 12 - winnerFirst;
            loserSecond = 12 - loserFirst;
            winnerTotal = 16;
            loserTotal = 12 + _random.Next(0, 3);
        }
        else
        {
            loserTotal = _random.Next(0, 12);
            winnerTotal = 13;
            // The first half has 12 rounds; the second ends when the winner reaches 13.
            winnerFirst = _random.Next(Math.Max(1, 12 - loserTotal), 13);
            loserFirst = 12 - winnerFirst;
            winnerSecond = winnerTotal - winnerFirst;
            loserSecond = loserTotal - loserFirst;
        }

        return new MatchMap
        {
            Order = order,
            MapName = mapName,
            OurScore = weWin ? winnerTotal : loserTotal,
            OpponentScore = weWin ? loserTotal : winnerTotal,
            OurFirstHalf = weWin ? winnerFirst : loserFirst,
            OpponentFirstHalf = weWin ? loserFirst : winnerFirst,
            OurSecondHalf = weWin ? winnerSecond : loserSecond,
            OpponentSecondHalf = weWin ? loserSecond : winnerSecond
        };
    }

    // Members who can play (not Coaches) in lineup order (the Owner first), each with their usual in-game role.
    private async Task<List<(string UserId, string Role)>> LineupPoolAsync(int teamId)
    {
        var team = await _db.Teams.Include(t => t.Members).SingleAsync(t => t.Id == teamId);
        var ids = team.Members.Select(m => m.UserId).ToList();
        var emails = await _userManager.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email ?? "");

        return team.Members
            .Where(m => m.Role.CanPlay())
            .OrderByDescending(m => m.UserId == team.OwnerId)
            .ThenBy(m => m.JoinedAt)
            .Select(m =>
            {
                var seed = SeedPlayers.FirstOrDefault(p => SeedEmail(p.Name) == emails.GetValueOrDefault(m.UserId));
                return (m.UserId, seed.Role ?? "Rifler");
            })
            .ToList();
    }

    // ---- Shared helpers ----

    private Team NewTeam(string name, string ownerId)
    {
        var team = new Team
        {
            Name = name,
            OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow,
            EventTypes = EventTypeCatalog.CreateDefaults()
        };
        // Scrims and recorded matches make the calendar more interesting to test.
        team.EventTypes.Add(EventTypeCatalog.Create(EventTypePreset.Scrim));
        team.EventTypes.Add(EventTypeCatalog.Create(EventTypePreset.RecordedMatch));
        team.Members.Add(new TeamMember { UserId = ownerId, Role = TeamRole.Captain, JoinedAt = DateTime.UtcNow });
        return team;
    }

    // Makes sure the team has every type the calendar generator uses, and returns their IDs by preset.
    private async Task<Dictionary<EventTypePreset, int>> EnsureEventTypesAsync(int teamId)
    {
        EventTypePreset[] needed =
        [
            EventTypePreset.Pracc, EventTypePreset.OfficialMatch, EventTypePreset.Scrim,
            EventTypePreset.VodReview, EventTypePreset.Theory, EventTypePreset.Other
        ];

        var types = await _db.TeamEventTypes.Where(t => t.TeamId == teamId && t.Preset != null).ToListAsync();
        var takenNames = types.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in needed.Where(p => types.All(t => t.Preset != p)))
        {
            var type = EventTypeCatalog.Create(preset);
            type.TeamId = teamId;
            // A custom type may already use the preset's name, which must be unique per team.
            if (takenNames.Contains(type.Name)) type.Name = $"{type.Name} (test)";
            _db.TeamEventTypes.Add(type);
            types.Add(type);
        }
        await _db.SaveChangesAsync();

        return types.Where(t => t.Preset.HasValue).ToDictionary(t => t.Preset!.Value, t => t.Id);
    }

    // The made-up players, created the first time they're needed. They have no password, so nobody can log in as them.
    private async Task<List<AppUser>> EnsureSeedPlayersAsync()
    {
        var users = new List<AppUser>();
        foreach (var (name, _) in SeedPlayers)
        {
            var email = SeedEmail(name);
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new AppUser { Email = email, UserName = name, EmailConfirmed = true, SteamId = await UnusedSteamIdAsync() };
                var created = await _userManager.CreateAsync(user);
                if (!created.Succeeded)
                {
                    // A real account already has this username.
                    user.UserName = $"{name}_test";
                    created = await _userManager.CreateAsync(user);
                    if (!created.Succeeded) continue;
                }
            }
            users.Add(user);
        }
        return users;
    }

    private async Task<string> UnusedSteamIdAsync()
    {
        while (true)
        {
            var id = "76561198" + _random.Next(100_000_000, 999_999_999).ToString();
            if (!await _userManager.Users.AnyAsync(u => u.SteamId == id))
                return id;
        }
    }

    private static string SeedEmail(string name) => $"seed-{name}@{SeedEmailDomain}";

    // How much better (positive) or worse (negative) than a coin flip the team is on each map.
    private Dictionary<string, double> MapStrengths()
    {
        var shuffled = MapPool.Maps.OrderBy(_ => _random.Next()).ToList();
        return shuffled
            .Select((map, i) => (map, strength: 0.25 - 0.5 * i / (shuffled.Count - 1)))
            .ToDictionary(x => x.map, x => x.strength);
    }

    // From the first day of last month to the last day of next month, in the tester's time zone.
    private static (DateOnly First, DateOnly Last) CalendarWindow(TimeZoneInfo timeZone)
    {
        var today = LocalToday(timeZone);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        return (thisMonth.AddMonths(-1), thisMonth.AddMonths(2).AddDays(-1));
    }

    private static DateOnly LocalToday(TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));

    private static DateTime ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        // A time skipped by a daylight saving change: move past the gap.
        if (timeZone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    private static TimeZoneInfo ResolveTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var timeZone)
            ? timeZone
            : TimeZoneInfo.Utc;

    private static bool TryReadMode(string? mode, out bool replace)
    {
        replace = string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase);
        return replace || string.Equals(mode, "add", StringComparison.OrdinalIgnoreCase);
    }

    private bool Chance(double probability) => _random.NextDouble() < probability;

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    private int PickWeighted(params (int Value, int Weight)[] options)
    {
        var roll = _random.Next(options.Sum(o => o.Weight));
        foreach (var (value, weight) in options)
        {
            if (roll < weight) return value;
            roll -= weight;
        }
        return options[^1].Value;
    }
}
