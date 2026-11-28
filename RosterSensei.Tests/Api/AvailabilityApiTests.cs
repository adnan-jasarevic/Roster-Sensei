using RosterSensei.Application.DTOs;
using System.Net;
using System.Net.Http.Json;

namespace RosterSensei.Tests.Api;

// The availability endpoints through HTTP: routing, the validation filter, and that a team sees what
// its members saved for themselves.
public class AvailabilityApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private static readonly string EveningsOff = new('0', 48);
    private static readonly string Evening = new string('0', 38) + new string('1', 4) + new string('0', 6);

    [Fact]
    public async Task A_week_saved_by_one_member_shows_in_the_teams_availability()
    {
        var alice = await _api.SignedInClientAsync("alice");
        var created = await alice.PostAsJsonAsync("/api/Teams", new CreateTeamDto { Name = "Alpha Team" });
        var teamId = (await created.Content.ReadFromJsonAsync<TeamDto>())!.Id;

        // Monday 19:00-21:00 in UTC.
        var save = await alice.PutAsJsonAsync("/api/availability/weekly", new SaveWeeklyAvailabilityDto
        {
            TimeZone = "Etc/UTC",
            Weekly = [Evening, EveningsOff, EveningsOff, EveningsOff, EveningsOff, EveningsOff, EveningsOff]
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var monday = new DateTime(2030, 1, 7, 0, 0, 0, DateTimeKind.Utc);
        var team = await alice.GetFromJsonAsync<TeamAvailabilityDto>($"/api/teams/{teamId}/availability?from={monday:O}&to={monday.AddDays(7):O}");

        Assert.Equal(5, team!.PlayersNeeded);
        Assert.True(Assert.Single(team.Members).HasFilledIn);
        Assert.Equal([monday.AddHours(19), monday.AddHours(19.5), monday.AddHours(20), monday.AddHours(20.5)], team.Slots.Select(s => s.Start));
    }

    [Fact]
    public async Task An_unknown_time_zone_returns_400()
    {
        var alice = await _api.SignedInClientAsync("alice");

        var save = await alice.PutAsJsonAsync("/api/availability/weekly", new SaveWeeklyAvailabilityDto
        {
            TimeZone = "Nowhere/Special",
            Weekly = Enumerable.Repeat(EveningsOff, 7).ToList()
        });

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
    }

    [Fact]
    public async Task Resetting_a_day_that_was_never_changed_returns_404()
    {
        var alice = await _api.SignedInClientAsync("alice");

        var reset = await alice.DeleteAsync("/api/availability/days/2030-01-07");

        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
    }
}
