using RosterSensei.Application.DTOs;
using RosterSensei.Application.Services;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Infrastructure.Repositories;
using RosterSensei.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace RosterSensei.Tests;

// Uses the scoreboard of a real scrim demo (Mirage, 17 - 7, all 24 rounds played, two coaches).
public class DemoImportTests : IDisposable
{
    private const string Parser = "demoparser2 0.42.3";
    private const string Fingerprint = "098cacfe224a39161c9af77c7c21b7009c9947c9c58a616771f015268d2a600f";

    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly string _maze;
    private readonly string _muciek;
    private readonly string _altekz;
    private readonly string _coach;
    private readonly string _player;
    private readonly string _outsider;
    private readonly int _teamId;

    public DemoImportTests()
    {
        _owner = _db.AddUser("owner");
        _maze = _db.AddUser("maze");
        _muciek = _db.AddUser("muciek");
        _altekz = _db.AddUser("altekz");
        _coach = _db.AddUser("kairi");
        _player = _db.AddUser("player");
        _outsider = _db.AddUser("outsider");
        _teamId = _db.AddTeam(_owner,
            (_maze, TeamRole.Player), (_muciek, TeamRole.Player), (_altekz, TeamRole.Officer),
            (_coach, TeamRole.Coach), (_player, TeamRole.Player));
        _db.SetSteamId(_maze, "76561198212412773");
        _db.SetSteamId(_muciek, "76561198259230534");
        _db.SetSteamId(_altekz, "76561198209342312");
        _db.SetSteamId(_coach, "76561197995658520");
        // On the other team in the demo, but not a member here.
        _db.SetSteamId(_outsider, "76561198339617378");
    }

    public void Dispose() => _db.Dispose();

    private static DemoPlayerDto P(string steamId, string name, int k, int d, int a, int dmg, int hs) =>
        new() { SteamId = steamId, Name = name, Kills = k, Deaths = d, Assists = a, Damage = dmg, HeadshotKills = hs };

    // The losing side comes first, as a demo can list them in either order.
    private static DemoSummaryDto Scrim(string fingerprint = Fingerprint, int? patch = 14175, string parser = Parser) => new()
    {
        ParserVersion = parser,
        PatchVersion = patch,
        Fingerprint = fingerprint,
        MapName = "de_mirage",
        Rounds = 24,
        Sides =
        {
            new DemoSideDto
            {
                Score = 7, FirstHalf = 4, SecondHalf = 3,
                Players = new()
                {
                    P("76561198339617378", "forkyz", 16, 19, 6, 2154, 13),
                    P("76561198102158293", "thragg", 16, 18, 3, 1430, 7),
                    P("76561198981293954", "30days", 12, 22, 5, 1438, 9),
                    P("76561199044291307", "nnnnnnnnn", 10, 18, 6, 1293, 3),
                    P("76561198860321247", "demente", 10, 19, 4, 913, 6)
                }
            },
            new DemoSideDto
            {
                Score = 17, FirstHalf = 8, SecondHalf = 9,
                Players = new()
                {
                    P("76561198212412773", "Maze", 23, 13, 11, 2588, 13),
                    P("76561198259230534", "Muciek", 20, 11, 8, 2228, 6),
                    P("76561198209342312", "Altekz", 20, 15, 1, 1678, 9),
                    P("76561198182462587", "szarkira", 16, 12, 7, 1808, 11),
                    P("76561198176422667", "helicopter", 16, 13, 5, 1586, 12)
                },
                Coaches = new()
                {
                    new DemoCoachDto { SteamId = "76561197995658520", Name = "COACH Kairi" },
                    new DemoCoachDto { SteamId = "76561197997387847", Name = "COACH MusambaN1" }
                }
            }
        }
    };

    private int AddMatch(MatchStatus status = MatchStatus.Scheduled, int? bestOf = 1, params string[] maps)
    {
        using var context = _db.CreateContext();
        var match = new Match
        {
            TeamId = _teamId, OpponentName = "Friends", ScheduledDate = DateTime.UtcNow, Status = status, Type = MatchKind.Scrim, BestOf = bestOf,
            Maps = maps.Select((m, i) => new MatchMap { Order = i + 1, MapName = m }).ToList()
        };
        context.Matches.Add(match);
        context.SaveChanges();
        return match.Id;
    }

    private async Task<MatchDto> Match(int matchId) =>
        (await _db.MatchService().GetTeamMatchesAsync(_owner, _teamId)).Data!.Single(m => m.Id == matchId);

    private async Task<MatchDto> Import(int matchId, DemoSummaryDto? demo = null, int mapOrder = 1, string? asUser = null)
    {
        var version = (await Match(matchId)).Version;
        var result = await _db.DemoImportService().ApplyAsync(asUser ?? _owner, _teamId, matchId,
            new ApplyDemoImportDto { Demo = demo ?? Scrim(), OurSide = 1, MapOrder = mapOrder, Version = version });
        Assert.True(result.Success, result.ErrorMessage);
        return result.Data!;
    }

    [Fact]
    public async Task Preview_finds_our_side_and_links_players_and_coaches_by_Steam_account()
    {
        var matchId = AddMatch(maps: "Mirage");

        var result = await _db.DemoImportService().PreviewAsync(_owner, _teamId, matchId, new PreviewDemoImportDto { Demo = Scrim() });

        Assert.True(result.Success, result.ErrorMessage);
        var preview = result.Data!;
        Assert.Equal(1, preview.OurSide);
        Assert.Equal(1, preview.MapOrder);
        Assert.False(preview.ReplacesImport);

        var ours = preview.Sides[1];
        Assert.Equal(new[] { "maze", "muciek", "altekz", null, null }, ours.Players.Select(p => p.Username));
        var maze = ours.Players[0];
        Assert.Equal(107.8m, maze.Adr);
        Assert.Equal(56.5m, maze.HeadshotPercentage);

        // forkyz has a linked Steam account, but isn't on this team.
        Assert.All(preview.Sides[0].Players, p => Assert.Null(p.UserId));

        Assert.Equal(new[] { "kairi", "COACH MusambaN1" }, preview.Coaches.Select(c => c.DisplayName));
        Assert.Equal(_coach, preview.Coaches[0].UserId);
        Assert.Null(preview.Coaches[1].UserId);
    }

    [Fact]
    public async Task Preview_has_no_side_when_nobody_from_the_team_is_in_the_demo()
    {
        var matchId = AddMatch();
        var demo = Scrim();
        demo.Sides[1].Players.RemoveRange(0, 3);

        var preview = (await _db.DemoImportService().PreviewAsync(_owner, _teamId, matchId, new PreviewDemoImportDto { Demo = demo })).Data!;

        Assert.Null(preview.OurSide);
    }

    [Fact]
    public async Task Import_saves_the_map_score_halves_lineup_stats_and_coaches()
    {
        var matchId = AddMatch(maps: "Mirage");

        var match = await Import(matchId);

        Assert.Equal("Finished", match.Status);
        Assert.Equal("17 - 7", match.FinalScore);
        var map = Assert.Single(match.Maps);
        Assert.Equal(("Mirage", 17, 7, 8, 4, 9, 3), (map.MapName, map.OurScore, map.OpponentScore, map.OurFirstHalf, map.OpponentFirstHalf, map.OurSecondHalf, map.OpponentSecondHalf));

        Assert.Equal(3, match.Lineup.Count);
        var maze = match.Lineup.Single(p => p.UserId == _maze);
        Assert.True(maze.Played);
        Assert.Equal((23, 13, 11, 107.8m, 56.5m), (maze.Kills!.Value, maze.Deaths!.Value, maze.Assists!.Value, maze.Adr!.Value, maze.HeadshotPercentage!.Value));
        var muciek = match.Lineup.Single(p => p.UserId == _muciek);
        Assert.Equal((92.8m, 30.0m), (muciek.Adr!.Value, muciek.HeadshotPercentage!.Value));

        Assert.Equal(new[] { "kairi", "COACH MusambaN1" }, match.Coaches.Select(c => c.DisplayName));
        Assert.Equal("76561197997387847", match.Coaches[1].SteamId);
        Assert.Equal(new[] { 1 }, match.ImportedMapOrders);
    }

    [Fact]
    public async Task Import_adds_the_map_when_the_match_has_none_yet()
    {
        var matchId = AddMatch();

        var match = await Import(matchId);

        Assert.Equal("Mirage", Assert.Single(match.Maps).MapName);
    }

    [Fact]
    public async Task Import_replaces_stats_entered_by_hand_but_keeps_the_rating()
    {
        var matchId = AddMatch(MatchStatus.Finished, maps: "Mirage");
        var set = await _db.MatchService().SetLineupAsync(_owner, _teamId, matchId, new SetMatchLineupDto
        {
            Players = new()
            {
                new LineupPlayerDto { UserId = _maze, Role = "AWPer", Rating = 1.45m, Kills = 1, Deaths = 1, Assists = 1, Adr = 1m, HeadshotPercentage = 1m },
                new LineupPlayerDto { UserId = _player, Played = false }
            }
        });
        Assert.True(set.Success, set.ErrorMessage);

        var match = await Import(matchId);

        var maze = match.Lineup.Single(p => p.UserId == _maze);
        Assert.Equal(("AWPer", 1.45m, 23), (maze.Role, maze.Rating!.Value, maze.Kills!.Value));
        // A substitute who isn't in the demo keeps their place.
        Assert.False(match.Lineup.Single(p => p.UserId == _player).Played);
    }

    [Fact]
    public async Task Importing_a_map_again_replaces_its_stats_instead_of_adding_them_up()
    {
        var matchId = AddMatch(maps: "Mirage");
        await Import(matchId);

        var corrected = Scrim(fingerprint: new string('a', 64));
        corrected.Sides[1].Players[0].Kills = 22;
        var match = await Import(matchId, corrected);

        Assert.Equal(22, match.Lineup.Single(p => p.UserId == _maze).Kills);
        Assert.Equal(new[] { 1 }, match.ImportedMapOrders);
    }

    [Fact]
    public async Task Stats_of_a_series_are_the_sums_over_its_imported_maps()
    {
        var matchId = AddMatch(bestOf: 3, maps: new[] { "Mirage", "Mirage" });
        await Import(matchId, mapOrder: 1);

        var second = Scrim(fingerprint: new string('b', 64));
        var match = await Import(matchId, second, mapOrder: 2);

        var maze = match.Lineup.Single(p => p.UserId == _maze);
        Assert.Equal((46, 26, 107.8m, 56.5m), (maze.Kills!.Value, maze.Deaths!.Value, maze.Adr!.Value, maze.HeadshotPercentage!.Value));
        // Two maps won.
        Assert.Equal("2 - 0", match.FinalScore);
        // Coaches are listed once.
        Assert.Equal(2, match.Coaches.Count);
    }

    [Fact]
    public async Task The_same_demo_cannot_be_imported_into_two_matches()
    {
        var first = AddMatch();
        var second = AddMatch();
        await Import(first);

        var version = (await Match(second)).Version;
        var result = await _db.DemoImportService().ApplyAsync(_owner, _teamId, second,
            new ApplyDemoImportDto { Demo = Scrim(), OurSide = 1, MapOrder = 1, Version = version });

        Assert.False(result.Success);
        Assert.Contains("already imported", result.ErrorMessage);
    }

    [Fact]
    public async Task Two_imports_of_the_same_demo_at_once_save_only_one()
    {
        var first = AddMatch();
        var second = AddMatch();

        // The second request loaded its match before the first one saved.
        await using var staleContext = _db.CreateContext();
        var repository = new MatchRepository(staleContext);
        var stale = (await repository.GetForEditAsync(_teamId, second))!;

        await Import(first);

        stale.DemoImports.Add(new MatchDemoImport { TeamId = _teamId, MapOrder = 1, MapName = "Mirage", Fingerprint = Fingerprint, ParserVersion = Parser, Rounds = 24, ImportedByUserId = _owner });
        Assert.False(await repository.UpdateAsync(stale, stale.Version));

        using var context = _db.CreateContext();
        Assert.Equal(1, await context.MatchDemoImports.CountAsync());
    }

    [Fact]
    public async Task A_demo_on_another_map_than_the_match_has_is_refused()
    {
        var matchId = AddMatch(maps: "Inferno");

        var result = await _db.DemoImportService().ApplyAsync(_owner, _teamId, matchId,
            new ApplyDemoImportDto { Demo = Scrim(), OurSide = 1, MapOrder = 1, Version = 0 });

        Assert.False(result.Success);
        Assert.Contains("Inferno", result.ErrorMessage);
    }

    [Fact]
    public async Task An_import_from_a_stale_match_version_is_refused()
    {
        var matchId = AddMatch();

        var result = await _db.DemoImportService().ApplyAsync(_owner, _teamId, matchId,
            new ApplyDemoImportDto { Demo = Scrim(), OurSide = 1, MapOrder = 1, Version = 5 });

        Assert.False(result.Success);
        Assert.Equal(Application.Common.ErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task Only_team_managers_can_import()
    {
        var matchId = AddMatch();

        var asPlayer = await _db.DemoImportService().PreviewAsync(_player, _teamId, matchId, new PreviewDemoImportDto { Demo = Scrim() });
        var asOutsider = await _db.DemoImportService().PreviewAsync(_outsider, _teamId, matchId, new PreviewDemoImportDto { Demo = Scrim() });

        Assert.Equal(Application.Common.ErrorType.Forbidden, asPlayer.ErrorType);
        Assert.Equal(Application.Common.ErrorType.Forbidden, asOutsider.ErrorType);
        // A Coach can import.
        await Import(matchId, asUser: _coach);
    }

    [Fact]
    public async Task A_cancelled_match_cannot_get_a_demo()
    {
        var matchId = AddMatch(MatchStatus.Cancelled);

        var result = await _db.DemoImportService().PreviewAsync(_owner, _teamId, matchId, new PreviewDemoImportDto { Demo = Scrim() });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Players_can_be_linked_by_hand_and_left_out()
    {
        var matchId = AddMatch();
        var version = (await Match(matchId)).Version;

        var result = await _db.DemoImportService().ApplyAsync(_owner, _teamId, matchId, new ApplyDemoImportDto
        {
            Demo = Scrim(), OurSide = 1, MapOrder = 1, Version = version,
            Players = new()
            {
                new DemoPlayerLinkDto { SteamId = "76561198212412773", UserId = _maze },
                // szarkira hasn't linked Steam: picked by hand.
                new DemoPlayerLinkDto { SteamId = "76561198182462587", UserId = _player },
                new DemoPlayerLinkDto { SteamId = "76561198259230534", UserId = null }
            }
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(16, result.Data!.Lineup.Single(p => p.UserId == _player).Kills);
        Assert.DoesNotContain(result.Data.Lineup, p => p.UserId == _muciek);
    }

    [Fact]
    public async Task A_player_from_the_other_side_or_a_Coach_cannot_be_linked()
    {
        var matchId = AddMatch();
        var service = _db.DemoImportService();

        var otherSide = await service.ApplyAsync(_owner, _teamId, matchId, new ApplyDemoImportDto
        {
            Demo = Scrim(), OurSide = 1, MapOrder = 1, Version = 0,
            Players = new() { new DemoPlayerLinkDto { SteamId = "76561198339617378", UserId = _player } }
        });
        var coach = await _db.DemoImportService().ApplyAsync(_owner, _teamId, matchId, new ApplyDemoImportDto
        {
            Demo = Scrim(), OurSide = 1, MapOrder = 1, Version = 0,
            Players = new() { new DemoPlayerLinkDto { SteamId = "76561198182462587", UserId = _coach } }
        });

        Assert.False(otherSide.Success);
        Assert.Equal("Coaches can't be put in a lineup.", coach.ErrorMessage);
    }

    [Fact]
    public async Task Coaches_can_be_set_by_hand_on_import()
    {
        var matchId = AddMatch();

        var result = await _db.DemoImportService().ApplyAsync(_owner, _teamId, matchId, new ApplyDemoImportDto
        {
            Demo = Scrim(), OurSide = 1, MapOrder = 1, Version = 0,
            Coaches = new() { new MatchCoachDto { Name = "Analyst Ana" } }
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("Analyst Ana", Assert.Single(result.Data!.Coaches).DisplayName);
    }

    [Fact]
    public async Task Removing_an_imported_map_in_an_edit_drops_its_demo()
    {
        var matchId = AddMatch(maps: "Mirage");
        var imported = await Import(matchId);

        var edit = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, matchId, new UpdateMatchDto
        {
            OpponentName = imported.OpponentName, ScheduledDate = imported.ScheduledDate, Status = MatchStatus.Finished, Type = MatchKind.Scrim,
            BestOf = 1, OurScore = 13, OpponentScore = 5, Maps = { new MatchMapDto { MapName = "Nuke", OurScore = 13, OpponentScore = 5 } },
            Version = imported.Version
        });

        Assert.True(edit.Success, edit.ErrorMessage);
        Assert.Empty(edit.Data!.ImportedMapOrders);
        // Coaches weren't sent, so they stay.
        Assert.Equal(2, edit.Data.Coaches.Count);
        using var context = _db.CreateContext();
        Assert.Equal(0, await context.Set<DemoPlayerStats>().CountAsync());
    }

    [Fact]
    public async Task Coaches_can_be_saved_with_a_match_and_must_be_members_or_named()
    {
        var service = _db.MatchService();
        var added = await service.AddMatchAsync(_owner, _teamId, new CreateMatchDto
        {
            OpponentName = "Friends", ScheduledDate = DateTime.UtcNow, Status = MatchStatus.Scheduled,
            Coaches = new() { new MatchCoachDto { UserId = _coach }, new MatchCoachDto { Name = "Guest coach" } }
        });
        var outsider = await _db.MatchService().AddMatchAsync(_owner, _teamId, new CreateMatchDto
        {
            OpponentName = "Friends", ScheduledDate = DateTime.UtcNow, Status = MatchStatus.Scheduled,
            Coaches = new() { new MatchCoachDto { UserId = _outsider } }
        });

        Assert.True(added.Success, added.ErrorMessage);
        Assert.Equal(new[] { "kairi", "Guest coach" }, added.Data!.Coaches.Select(c => c.DisplayName));
        Assert.False(outsider.Success);
    }

    [Fact]
    public async Task A_read_failure_on_a_newer_patch_pauses_that_parser_version()
    {
        var matchId = AddMatch();
        await Import(matchId);
        var service = _db.DemoImportService();

        // A broken file on a patch already imported from doesn't pause anything.
        var sameP = await service.ReportReadFailureAsync(_owner, _teamId, new DemoReadFailureDto { ParserVersion = Parser, PatchVersion = 14175 });
        Assert.False(sameP.Data!.Paused);

        var newer = await _db.DemoImportService().ReportReadFailureAsync(_owner, _teamId, new DemoReadFailureDto { ParserVersion = Parser, PatchVersion = 14200 });
        Assert.True(newer.Data!.Paused);
        Assert.True((await _db.DemoImportService().GetStatusAsync(Parser)).Data!.Paused);

        var other = AddMatch();
        var refused = await _db.DemoImportService().PreviewAsync(_owner, _teamId, other, new PreviewDemoImportDto { Demo = Scrim(fingerprint: new string('c', 64)) });
        Assert.Equal(DemoImportService.PausedMessage, refused.ErrorMessage);

        // A new release with an updated parser starts unpaused.
        Assert.False((await _db.DemoImportService().GetStatusAsync("demoparser2 0.43.0")).Data!.Paused);
    }

    [Fact]
    public async Task Reporting_a_failure_twice_at_once_keeps_one_pause()
    {
        await Import(AddMatch());
        var report = new DemoReadFailureDto { ParserVersion = Parser, PatchVersion = 15000 };

        await Task.WhenAll(
            _db.DemoImportService().ReportReadFailureAsync(_owner, _teamId, report),
            _db.DemoImportService().ReportReadFailureAsync(_owner, _teamId, report));

        using var context = _db.CreateContext();
        Assert.Equal(1, await context.DemoParserStatuses.CountAsync());
    }

    [Fact]
    public async Task Only_team_managers_can_report_failures()
    {
        var result = await _db.DemoImportService().ReportReadFailureAsync(_player, _teamId, new DemoReadFailureDto { ParserVersion = Parser, PatchVersion = 1 });

        Assert.Equal(Application.Common.ErrorType.Forbidden, result.ErrorType);
    }

    [Theory]
    [InlineData("de_mirage", "Mirage")]
    [InlineData("de_dust2", "Dust2")]
    [InlineData("cs_office", "Office")]
    [InlineData("workshop_map", "Workshop_map")]
    public void Map_names_from_demos_are_spelled_like_the_map_pool(string demoName, string expected) =>
        Assert.Equal(expected, DemoImportService.NormalizeMapName(demoName));

    [Fact]
    public void The_real_scrim_passes_validation()
    {
        var result = new DemoSummaryDtoValidator().Validate(Scrim());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Broken_demo_summaries_are_refused()
    {
        var validator = new DemoSummaryDtoValidator();

        var wrongRounds = Scrim(); wrongRounds.Rounds = 23;
        var badHalves = Scrim(); badHalves.Sides[0].FirstHalf = 5;
        var moreHeadshots = Scrim(); moreHeadshots.Sides[0].Players[0].HeadshotKills = 99;
        var badSteam = Scrim(); badSteam.Sides[0].Players[0].SteamId = "123";
        var badFingerprint = Scrim(fingerprint: "ABC");
        var twice = Scrim(); twice.Sides[0].Players[0].SteamId = twice.Sides[1].Players[0].SteamId;

        foreach (var demo in new[] { wrongRounds, badHalves, moreHeadshots, badSteam, badFingerprint, twice })
            Assert.False(validator.Validate(demo).IsValid);
    }
}
