using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

// Editing a match: any status can change to another (recording a result, cancelling, reopening,
// correcting), and a save over someone else's newer edit is refused.
public class MatchEditTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly string _officer;
    private readonly string _player;
    private readonly string _outsider;
    private readonly int _teamId;

    public MatchEditTests()
    {
        _owner = _db.AddUser("owner");
        _officer = _db.AddUser("officer");
        _player = _db.AddUser("player");
        _outsider = _db.AddUser("outsider");
        _teamId = _db.AddTeam(_owner, (_officer, TeamRole.Officer), (_player, TeamRole.Player));
    }

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(19);

    private async Task<MatchDto> AddScheduledAsync()
    {
        var created = await _db.MatchService().AddMatchAsync(_owner, _teamId, new CreateMatchDto
        {
            OpponentName = "Kyoto Kitsune",
            ScheduledDate = Yesterday,
            Status = MatchStatus.Scheduled,
            BestOf = 3,
            Maps = [new() { MapName = "Mirage" }, new() { MapName = "Nuke" }, new() { MapName = "Inferno" }],
            Lineup = [new() { UserId = _owner, Role = "IGL" }, new() { UserId = _player, Played = false }]
        });
        Assert.True(created.Success, created.ErrorMessage);
        return created.Data!;
    }

    // The match as the form would send it back, with the result filled in.
    private static UpdateMatchDto Result(MatchDto match, List<LineupPlayerDto>? lineup = null) => new()
    {
        Version = match.Version,
        OpponentName = match.OpponentName,
        ScheduledDate = match.ScheduledDate,
        PlayedDate = match.ScheduledDate,
        Status = MatchStatus.Finished,
        BestOf = 3,
        Maps =
        [
            new() { MapName = "Mirage", OurScore = 13, OpponentScore = 7 },
            new() { MapName = "Nuke", OurScore = 9, OpponentScore = 13 },
            new() { MapName = "Inferno", OurScore = 13, OpponentScore = 11 }
        ],
        Lineup = lineup
    };

    private async Task<MatchDto> StoredAsync(int matchId) =>
        (await _db.MatchService().GetTeamMatchesAsync(_owner, _teamId)).Data!.Single(m => m.Id == matchId);

    [Fact]
    public async Task A_scheduled_match_gets_its_result_with_ratings()
    {
        var match = await AddScheduledAsync();

        var updated = await _db.MatchService().UpdateMatchAsync(_officer, _teamId, match.Id, Result(match,
            [new() { UserId = _owner, Role = "IGL", Rating = 1.21m }, new() { UserId = _officer, Rating = 0.98m }]));

        Assert.True(updated.Success, updated.ErrorMessage);
        var stored = await StoredAsync(match.Id);
        Assert.Equal("Finished", stored.Status);
        Assert.Equal("2 - 1", stored.FinalScore);
        Assert.Equal("Win", stored.MatchOutcome);
        Assert.Equal(["Mirage", "Nuke", "Inferno"], stored.Maps.Select(m => m.MapName));
        Assert.Equal(1.21m, stored.Lineup.Single(p => p.UserId == _owner).Rating);
        Assert.Equal(match.Version + 1, stored.Version);
    }

    [Fact]
    public async Task A_result_without_a_lineup_keeps_the_lineup_that_was_there()
    {
        var match = await AddScheduledAsync();

        Assert.True((await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id, Result(match))).Success);

        var lineup = (await StoredAsync(match.Id)).Lineup;
        Assert.Equal("IGL", lineup.Single(p => p.UserId == _owner).Role);
        Assert.False(lineup.Single(p => p.UserId == _player).Played);
    }

    [Fact]
    public async Task A_result_can_be_corrected_later()
    {
        var match = await AddScheduledAsync();
        var first = (await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id, Result(match))).Data!;

        var correction = Result(first);
        correction.Maps[2].OurScore = 8;
        correction.Maps[2].OpponentScore = 13;
        var second = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id, correction);

        Assert.True(second.Success, second.ErrorMessage);
        Assert.Equal("1 - 2", second.Data!.FinalScore);
        Assert.Equal("Loss", second.Data.MatchOutcome);
    }

    [Theory]
    [InlineData(MatchStatus.Scheduled)]
    [InlineData(MatchStatus.Cancelled)]
    public async Task Leaving_finished_clears_the_score_played_date_and_ratings(MatchStatus status)
    {
        var match = await AddScheduledAsync();
        var finished = (await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id,
            Result(match, [new() { UserId = _owner, Rating = 1.5m }]))).Data!;

        var reopen = Result(finished);
        reopen.Status = status;
        reopen.PlayedDate = null;
        reopen.Maps = finished.Maps.Select(m => new MatchMapDto { MapName = m.MapName }).ToList();
        var result = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id, reopen);

        Assert.True(result.Success, result.ErrorMessage);
        var stored = await StoredAsync(match.Id);
        Assert.Equal(status.ToString(), stored.Status);
        Assert.Null(stored.FinalScore);
        Assert.Null(stored.MatchOutcome);
        Assert.Null(stored.PlayedDate);
        Assert.Null(Assert.Single(stored.Lineup).Rating);
    }

    [Fact]
    public async Task A_save_from_an_older_version_is_refused()
    {
        var match = await AddScheduledAsync();
        Assert.True((await _db.MatchService().UpdateMatchAsync(_officer, _teamId, match.Id, Result(match))).Success);

        // The owner opened the form before the officer saved.
        var stale = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id, Result(match));

        Assert.False(stale.Success);
        Assert.Equal(ErrorType.Conflict, stale.ErrorType);
        Assert.Equal(match.Version + 1, (await StoredAsync(match.Id)).Version);
    }

    [Fact]
    public async Task Two_saves_from_the_same_version_at_once_keep_exactly_one()
    {
        var match = await AddScheduledAsync();

        var results = await Task.WhenAll(
            _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id, Result(match)),
            _db.MatchService().UpdateMatchAsync(_officer, _teamId, match.Id, Result(match)));

        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => r.ErrorType == ErrorType.Conflict);
    }

    [Fact]
    public async Task Only_the_owner_captain_and_officers_can_edit()
    {
        var match = await AddScheduledAsync();

        var asPlayer = await _db.MatchService().UpdateMatchAsync(_player, _teamId, match.Id, Result(match));
        var asOutsider = await _db.MatchService().UpdateMatchAsync(_outsider, _teamId, match.Id, Result(match));

        Assert.Equal(ErrorType.Forbidden, asPlayer.ErrorType);
        Assert.Equal(ErrorType.Forbidden, asOutsider.ErrorType);
    }

    [Fact]
    public async Task A_match_of_another_team_is_not_found()
    {
        var match = await AddScheduledAsync();
        var otherTeam = _db.AddTeam(_outsider);

        var result = await _db.MatchService().UpdateMatchAsync(_outsider, otherTeam, match.Id, Result(match));

        Assert.Equal(ErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task The_lineup_takes_team_members_and_players_already_in_it()
    {
        var match = await AddScheduledAsync();
        // The player leaves the team, but stays in this match's history.
        Assert.True((await _db.TeamService().LeaveTeamAsync(_teamId, _player)).Success);

        var keepsFormer = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id,
            Result(match, [new() { UserId = _owner }, new() { UserId = _player, Played = false }]));
        Assert.True(keepsFormer.Success, keepsFormer.ErrorMessage);

        var addsOutsider = await _db.MatchService().UpdateMatchAsync(_owner, _teamId, match.Id,
            Result(keepsFormer.Data!, [new() { UserId = _outsider }]));
        Assert.Equal("Every player in the lineup must be a member of the team.", addsOutsider.ErrorMessage);
    }

    [Fact]
    public void An_edit_follows_the_rules_of_adding_a_match()
    {
        var validator = new UpdateMatchDtoValidator();
        var match = new UpdateMatchDto { OpponentName = "Osaka Oni", ScheduledDate = Yesterday, Status = MatchStatus.Cancelled, OurScore = 13 };

        Assert.Contains(validator.Validate(match).Errors, e => e.ErrorMessage == "A cancelled match cannot have a score.");
        match.OpponentName = "";
        Assert.Contains(validator.Validate(match).Errors, e => e.ErrorMessage == "The opponent name is required.");
    }

    [Fact]
    public void The_removed_live_status_is_refused()
    {
        var match = new CreateMatchDto { OpponentName = "Osaka Oni", ScheduledDate = Yesterday, Status = (MatchStatus)1 };

        Assert.Contains(new CreateMatchDtoValidator().Validate(match).Errors, e => e.ErrorMessage == "Invalid match status.");
    }
}
