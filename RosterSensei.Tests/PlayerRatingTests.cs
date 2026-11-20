using RosterSensei.Application.DTOs;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

// Player ratings: entered with the result in Add Match or later in the lineup, only for players who
// played in a finished match, from 0.01 to 99.99 with at most two decimals.
public class PlayerRatingTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly string _player;
    private readonly string _outsider;
    private readonly int _teamId;

    public PlayerRatingTests()
    {
        _owner = _db.AddUser("owner");
        _player = _db.AddUser("player");
        _outsider = _db.AddUser("outsider");
        _teamId = _db.AddTeam(_owner, (_player, TeamRole.Player));
    }

    public void Dispose() => _db.Dispose();

    private int AddMatch(MatchStatus status)
    {
        using var context = _db.CreateContext();
        var match = new Match { TeamId = _teamId, OpponentName = "Kyoto Kitsune", ScheduledDate = DateTime.UtcNow, Status = status };
        context.Matches.Add(match);
        context.SaveChanges();
        return match.Id;
    }

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

    private static LineupPlayerDto Player(string userId, decimal? rating, bool played = true) =>
        new() { UserId = userId, Played = played, Rating = rating };

    [Fact]
    public async Task Ratings_entered_with_the_result_are_saved_and_returned()
    {
        var created = await _db.MatchService().AddMatchAsync(_owner, _teamId, FinishedMatch(Player(_owner, 1.25m), Player(_player, null)));
        Assert.True(created.Success, created.ErrorMessage);
        Assert.Equal(1.25m, created.Data!.Lineup.Single(p => p.UserId == _owner).Rating);

        var matches = await _db.MatchService().GetTeamMatchesAsync(_player, _teamId);
        var lineup = matches.Data!.Single().Lineup;
        Assert.Equal(1.25m, lineup.Single(p => p.UserId == _owner).Rating);
        Assert.Null(lineup.Single(p => p.UserId == _player).Rating);
        Assert.Equal("player", lineup.Single(p => p.UserId == _player).Username);
    }

    [Fact]
    public async Task A_match_can_still_be_added_without_any_players()
    {
        var created = await _db.MatchService().AddMatchAsync(_owner, _teamId, FinishedMatch());

        Assert.True(created.Success, created.ErrorMessage);
        Assert.Empty(created.Data!.Lineup);
    }

    [Fact]
    public async Task A_lineup_with_the_result_must_be_team_members()
    {
        var created = await _db.MatchService().AddMatchAsync(_owner, _teamId, FinishedMatch(Player(_outsider, 1.0m)));

        Assert.False(created.Success);
        Assert.Equal("Every player in the lineup must be a member of the team.", created.ErrorMessage);
    }

    [Fact]
    public async Task Ratings_can_be_added_and_changed_later_in_the_lineup()
    {
        var matchId = AddMatch(MatchStatus.Finished);
        var service = _db.MatchService();

        var first = await service.SetLineupAsync(_owner, _teamId, matchId, new SetMatchLineupDto { Players = [Player(_player, 0.87m)] });
        Assert.True(first.Success, first.ErrorMessage);

        var second = await service.SetLineupAsync(_owner, _teamId, matchId, new SetMatchLineupDto { Players = [Player(_player, 1.42m)] });
        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal(1.42m, Assert.Single(second.Data!).Rating);
    }

    [Theory]
    [InlineData(MatchStatus.Scheduled)]
    [InlineData(MatchStatus.Cancelled)]
    public async Task A_match_that_is_not_finished_cannot_be_rated(MatchStatus status)
    {
        var matchId = AddMatch(status);

        var result = await _db.MatchService().SetLineupAsync(_owner, _teamId, matchId, new SetMatchLineupDto { Players = [Player(_player, 1.0m)] });

        Assert.False(result.Success);
        Assert.Equal("Ratings and stats can only be added to a finished match.", result.ErrorMessage);
    }

    [Fact]
    public async Task A_match_that_is_not_finished_still_takes_a_lineup_without_ratings()
    {
        var matchId = AddMatch(MatchStatus.Scheduled);

        var result = await _db.MatchService().SetLineupAsync(_owner, _teamId, matchId, new SetMatchLineupDto { Players = [Player(_player, null)] });

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Theory]
    [InlineData("0.01", true)]
    [InlineData("99.99", true)]
    [InlineData("1.05", true)]
    [InlineData("0", false)]
    [InlineData("100", false)]
    [InlineData("-1", false)]
    [InlineData("1.005", false)]
    public void Validator_keeps_ratings_between_0_01_and_99_99_with_two_decimals(string rating, bool valid)
    {
        var dto = new SetMatchLineupDto { Players = [Player("a", decimal.Parse(rating, System.Globalization.CultureInfo.InvariantCulture))] };

        Assert.Equal(valid, new SetMatchLineupDtoValidator().Validate(dto).IsValid);
    }

    [Fact]
    public void Validator_refuses_a_rating_for_a_substitute_who_did_not_play()
    {
        var dto = new SetMatchLineupDto { Players = [Player("a", 1.0m, played: false)] };

        Assert.False(new SetMatchLineupDtoValidator().Validate(dto).IsValid);
    }

    [Fact]
    public void Validator_refuses_ratings_on_a_new_match_that_is_not_finished()
    {
        var dto = FinishedMatch(Player("a", 1.0m));
        dto.Status = MatchStatus.Scheduled;
        dto.OurScore = 0;
        dto.OpponentScore = 0;

        var result = new CreateMatchDtoValidator().Validate(dto);

        Assert.Contains(result.Errors, e => e.ErrorMessage == "Ratings and stats can only be added to a finished match.");
    }

    [Fact]
    public void Validator_applies_the_lineup_rules_to_a_new_match()
    {
        var duplicate = FinishedMatch(Player("a", null), Player("a", null));
        var tooMany = FinishedMatch(Enumerable.Range(0, 11).Select(i => Player($"p{i}", null)).ToArray());

        Assert.False(new CreateMatchDtoValidator().Validate(duplicate).IsValid);
        Assert.False(new CreateMatchDtoValidator().Validate(tooMany).IsValid);
    }
}
