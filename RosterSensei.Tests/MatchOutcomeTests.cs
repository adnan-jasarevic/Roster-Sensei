using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

// The dashboard, matches and statistics endpoints all report results as "Win", "Loss" or "Draw",
// with the score written the same way. A match that isn't finished has neither.
public class MatchOutcomeTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly string _owner;
    private readonly int _teamId;

    public MatchOutcomeTests()
    {
        _owner = _db.AddUser("owner");
        _teamId = _db.AddTeam(_owner);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(16, 10, "Win")]
    [InlineData(8, 13, "Loss")]
    [InlineData(15, 15, "Draw")]
    public async Task Every_endpoint_reports_the_same_outcome_and_score(int ours, int theirs, string expected)
    {
        await AddFinishedMatchAsync(ours, theirs);
        var expectedScore = $"{ours} - {theirs}";

        var matches = (await _db.MatchService().GetTeamMatchesAsync(_owner, _teamId)).Data!;
        var dashboard = (await _db.TeamService().GetTeamDashboardAsync(_teamId, _owner)).Data!;
        var stats = (await _db.MatchService().GetTeamStatsAsync(_owner, _teamId, null)).Data!;

        Assert.Equal(expected, Assert.Single(matches).MatchOutcome);
        Assert.Equal(expectedScore, matches[0].FinalScore);
        Assert.Equal(expected, Assert.Single(dashboard.RecentMatches).MatchOutcome);
        Assert.Equal(expectedScore, dashboard.RecentMatches[0].FinalScore);
        Assert.Equal(expected, Assert.Single(stats.Form).MatchOutcome);
    }

    [Theory]
    [InlineData(2, 1, MatchOutcome.Win)]
    [InlineData(0, 2, MatchOutcome.Loss)]
    [InlineData(1, 1, MatchOutcome.Draw)]
    public void Outcome_follows_the_series_score(int ours, int theirs, MatchOutcome expected)
    {
        var match = new Match { Status = MatchStatus.Finished, OurScore = ours, OpponentScore = theirs };
        Assert.Equal(expected, match.Outcome);
    }

    [Theory]
    [InlineData(MatchStatus.Scheduled)]
    [InlineData(MatchStatus.Cancelled)]
    public void A_match_that_isnt_finished_has_no_outcome_or_score(MatchStatus status)
    {
        var match = new Match { Status = status, OurScore = 0, OpponentScore = 0 };
        Assert.False(match.HasResult);
        Assert.Null(match.Outcome);
        Assert.Null(match.FinalScore);
    }

    // Upcoming (still to be played) and "Needs result" (time passed, no score yet) matches both
    // come back without a result, not as a 0 - 0 draw.
    [Theory]
    [InlineData(3)]
    [InlineData(-3)]
    public async Task An_unscored_match_has_no_outcome_or_score_on_any_endpoint(int daysFromNow)
    {
        await AddMatchAsync(MatchStatus.Scheduled, DateTime.UtcNow.AddDays(daysFromNow));

        var match = Assert.Single((await _db.MatchService().GetTeamMatchesAsync(_owner, _teamId)).Data!);
        Assert.Equal("Scheduled", match.Status);
        Assert.Null(match.MatchOutcome);
        Assert.Null(match.FinalScore);

        var dashboard = (await _db.TeamService().GetTeamDashboardAsync(_teamId, _owner)).Data!;
        Assert.Empty(dashboard.RecentMatches);
        Assert.All(dashboard.UpcomingMatches, m =>
        {
            Assert.Null(m.MatchOutcome);
            Assert.Null(m.FinalScore);
        });
    }

    // One win plus matches without a score: the win rate is 100%, not dragged down by phantom draws.
    [Fact]
    public async Task Unscored_matches_are_not_counted_in_stats_or_win_rate()
    {
        await AddFinishedMatchAsync(13, 7);
        await AddMatchAsync(MatchStatus.Scheduled, DateTime.UtcNow.AddDays(2));   // upcoming
        await AddMatchAsync(MatchStatus.Scheduled, DateTime.UtcNow.AddDays(-2));  // needs result
        await AddMatchAsync(MatchStatus.Cancelled, DateTime.UtcNow.AddDays(-4));

        var dashboard = (await _db.TeamService().GetTeamDashboardAsync(_teamId, _owner)).Data!;
        Assert.Equal(1, dashboard.TotalMatches);
        Assert.Equal(100, dashboard.WinRatePercentage);
        Assert.Equal(1, dashboard.UpcomingMatchesCount);
        Assert.Equal("Win", Assert.Single(dashboard.RecentMatches).MatchOutcome);

        var stats = (await _db.MatchService().GetTeamStatsAsync(_owner, _teamId, null)).Data!;
        Assert.Equal(1, stats.MatchesPlayed);
        var point = Assert.Single(stats.Form);
        Assert.Equal("Win", point.MatchOutcome);
        Assert.Equal(100, point.WinRatePercentage);
        Assert.DoesNotContain(stats.Maps, m => m.Draws > 0);
    }

    private async Task AddMatchAsync(MatchStatus status, DateTime scheduledDate)
    {
        var dto = new CreateMatchDto
        {
            OpponentName = "Opponent",
            ScheduledDate = scheduledDate,
            Status = status,
            Type = MatchKind.Scrim,
            BestOf = 1,
            Maps = [new MatchMapDto { MapName = "Mirage" }]
        };
        var result = await _db.MatchService().AddMatchAsync(_owner, _teamId, dto);
        Assert.True(result.Success, result.ErrorMessage);
    }

    private async Task AddFinishedMatchAsync(int ours, int theirs)
    {
        var dto = new CreateMatchDto
        {
            OpponentName = "Opponent",
            ScheduledDate = DateTime.UtcNow.AddDays(-1),
            Status = MatchStatus.Finished,
            Type = MatchKind.Scrim,
            OurScore = ours,
            OpponentScore = theirs,
            BestOf = 1
        };
        var result = await _db.MatchService().AddMatchAsync(_owner, _teamId, dto);
        Assert.True(result.Success, result.ErrorMessage);
    }
}
