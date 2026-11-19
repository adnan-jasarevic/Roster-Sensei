using RosterSensei.Application.DTOs;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

// Kills, deaths, assists, ADR and HS% per player per match: same rules as the rating (players who
// played, finished matches only), entered with the result or later in the lineup.
public class PlayerMatchStatsTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly string _player;
    private readonly int _teamId;

    public PlayerMatchStatsTests()
    {
        _owner = _db.AddUser("owner");
        _player = _db.AddUser("player");
        _teamId = _db.AddTeam(_owner, (_player, TeamRole.Player));
    }

    public void Dispose() => _db.Dispose();

    private int AddMatch(MatchStatus status)
    {
        using var context = _db.CreateContext();
        var match = new Match { TeamId = _teamId, OpponentName = "Kyoto Kitsune", ScheduledDate = DateTime.UtcNow.AddDays(-1), Status = status };
        context.Matches.Add(match);
        context.SaveChanges();
        return match.Id;
    }

    private static LineupPlayerDto Line(string userId, int? kills = 21, int? deaths = 15, int? assists = 4, decimal? adr = 88.4m, decimal? hs = 47.6m, bool played = true) =>
        new() { UserId = userId, Played = played, Kills = kills, Deaths = deaths, Assists = assists, Adr = adr, HeadshotPercentage = hs };

    private static CreateMatchDto FinishedMatch(params LineupPlayerDto[] lineup) => new()
    {
        OpponentName = "Osaka Oni",
        ScheduledDate = DateTime.UtcNow.AddDays(-1),
        PlayedDate = DateTime.UtcNow.AddDays(-1),
        Status = MatchStatus.Finished,
        BestOf = 1,
        OurScore = 13,
        OpponentScore = 9,
        Lineup = lineup.ToList()
    };

    [Fact]
    public async Task Stats_entered_with_the_result_are_saved_and_returned_to_every_member()
    {
        var created = await _db.MatchService().AddMatchAsync(_owner, _teamId, FinishedMatch(Line(_owner), Line(_player, kills: null, adr: null)));
        Assert.True(created.Success, created.ErrorMessage);

        var lineup = (await _db.MatchService().GetTeamMatchesAsync(_player, _teamId)).Data!.Single().Lineup;
        var owner = lineup.Single(p => p.UserId == _owner);
        Assert.Equal((21, 15, 4, 88.4m, 47.6m), (owner.Kills, owner.Deaths, owner.Assists, owner.Adr, owner.HeadshotPercentage));

        // Each value is optional on its own.
        var player = lineup.Single(p => p.UserId == _player);
        Assert.Null(player.Kills);
        Assert.Null(player.Adr);
        Assert.Equal(15, player.Deaths);
    }

    [Fact]
    public async Task Stats_can_be_added_and_changed_later_in_the_lineup()
    {
        var matchId = AddMatch(MatchStatus.Finished);
        var service = _db.MatchService();

        Assert.True((await service.SetLineupAsync(_owner, _teamId, matchId, new SetMatchLineupDto { Players = [Line(_player)] })).Success);
        var second = await service.SetLineupAsync(_owner, _teamId, matchId, new SetMatchLineupDto { Players = [Line(_player, kills: 30, adr: 112.5m)] });

        Assert.True(second.Success, second.ErrorMessage);
        var saved = Assert.Single(second.Data!);
        Assert.Equal(30, saved.Kills);
        Assert.Equal(112.5m, saved.Adr);
    }

    [Theory]
    [InlineData(MatchStatus.Scheduled)]
    [InlineData(MatchStatus.Cancelled)]
    public async Task A_match_that_is_not_finished_cannot_get_stats(MatchStatus status)
    {
        var matchId = AddMatch(status);

        var result = await _db.MatchService().SetLineupAsync(_owner, _teamId, matchId,
            new SetMatchLineupDto { Players = [Line(_player, kills: null, deaths: null, assists: null, adr: null, hs: 50m)] });

        Assert.False(result.Success);
        Assert.Equal("Ratings and stats can only be added to a finished match.", result.ErrorMessage);
    }

    [Fact]
    public async Task Moving_a_match_away_from_finished_clears_its_stats()
    {
        var created = (await _db.MatchService().AddMatchAsync(_owner, _teamId, FinishedMatch(Line(_owner)))).Data!;

        var edited = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, created.Id, new UpdateMatchDto
        {
            Version = created.Version,
            OpponentName = created.OpponentName,
            ScheduledDate = created.ScheduledDate,
            Status = MatchStatus.Scheduled,
            BestOf = 1
        });

        Assert.True(edited.Success, edited.ErrorMessage);
        var owner = Assert.Single(edited.Data!.Lineup);
        Assert.Null(owner.Kills);
        Assert.Null(owner.Deaths);
        Assert.Null(owner.Assists);
        Assert.Null(owner.Adr);
        Assert.Null(owner.HeadshotPercentage);
    }

    [Fact]
    public async Task An_edit_that_sends_the_lineup_keeps_the_stats_it_sends()
    {
        var created = (await _db.MatchService().AddMatchAsync(_owner, _teamId, FinishedMatch(Line(_owner)))).Data!;
        var dto = new UpdateMatchDto
        {
            Version = created.Version,
            OpponentName = created.OpponentName,
            ScheduledDate = created.ScheduledDate,
            PlayedDate = created.ScheduledDate,
            Status = MatchStatus.Finished,
            BestOf = 1,
            OurScore = 13,
            OpponentScore = 11,
            Lineup = [Line(_owner, kills: 25)]
        };

        var edited = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, created.Id, dto);

        Assert.True(edited.Success, edited.ErrorMessage);
        var owner = Assert.Single(edited.Data!.Lineup);
        Assert.Equal(25, owner.Kills);
        Assert.Equal(88.4m, owner.Adr);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(999, true)]
    [InlineData(-1, false)]
    [InlineData(1000, false)]
    public void Validator_keeps_kills_deaths_and_assists_between_0_and_999(int value, bool valid)
    {
        var validator = new SetMatchLineupDtoValidator();

        Assert.Equal(valid, validator.Validate(new SetMatchLineupDto { Players = [Line("a", kills: value)] }).IsValid);
        Assert.Equal(valid, validator.Validate(new SetMatchLineupDto { Players = [Line("a", deaths: value)] }).IsValid);
        Assert.Equal(valid, validator.Validate(new SetMatchLineupDto { Players = [Line("a", assists: value)] }).IsValid);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("999.9", true)]
    [InlineData("75.5", true)]
    [InlineData("-0.1", false)]
    [InlineData("1000", false)]
    [InlineData("75.55", false)]
    public void Validator_keeps_adr_between_0_and_999_9_with_one_decimal(string adr, bool valid)
    {
        var dto = new SetMatchLineupDto { Players = [Line("a", adr: decimal.Parse(adr, System.Globalization.CultureInfo.InvariantCulture))] };

        Assert.Equal(valid, new SetMatchLineupDtoValidator().Validate(dto).IsValid);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("48.3", true)]
    [InlineData("100.1", false)]
    [InlineData("-1", false)]
    [InlineData("48.25", false)]
    public void Validator_keeps_hs_between_0_and_100_with_one_decimal(string hs, bool valid)
    {
        var dto = new SetMatchLineupDto { Players = [Line("a", hs: decimal.Parse(hs, System.Globalization.CultureInfo.InvariantCulture))] };

        Assert.Equal(valid, new SetMatchLineupDtoValidator().Validate(dto).IsValid);
    }

    [Fact]
    public void Validator_refuses_stats_for_a_substitute_who_did_not_play()
    {
        var dto = new SetMatchLineupDto { Players = [Line("a", kills: null, deaths: null, assists: 2, adr: null, hs: null, played: false)] };

        var result = new SetMatchLineupDtoValidator().Validate(dto);

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Only a player who played can have stats.");
    }

    [Fact]
    public void Validator_refuses_stats_on_a_new_match_that_is_not_finished()
    {
        var dto = FinishedMatch(Line("a", kills: 10, deaths: null, assists: null, adr: null, hs: null));
        dto.Status = MatchStatus.Scheduled;
        dto.OurScore = 0;
        dto.OpponentScore = 0;

        var result = new CreateMatchDtoValidator().Validate(dto);

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Ratings and stats can only be added to a finished match.");
    }
}
