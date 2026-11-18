using RosterSensei.Application.DTOs;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace RosterSensei.Tests.Api;

// PUT /api/Matches/{teamId}/{matchId} through HTTP: routing, the validation filter and the status codes.
public class MatchEditApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<(HttpClient Client, int TeamId, MatchDto Match)> ScheduledMatchAsync()
    {
        var client = await _api.SignedInClientAsync("alice");
        var created = await client.PostAsJsonAsync("/api/Teams", new CreateTeamDto { Name = "Alpha Team" });
        var teamId = (await created.Content.ReadFromJsonAsync<TeamDto>())!.Id;

        var added = await client.PostAsync($"/api/Matches/{teamId}", Json("""
            { "opponentName": "Kyoto Kitsune", "scheduledDate": "2026-11-01T18:00:00Z", "status": 0, "type": 0, "bestOf": 1 }
            """));
        added.EnsureSuccessStatusCode();
        return (client, teamId, (await added.Content.ReadFromJsonAsync<MatchDto>())!);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static StringContent Finished(int version) => Json($$"""
        { "version": {{version}}, "opponentName": "Kyoto Kitsune", "scheduledDate": "2026-11-01T18:00:00Z",
          "playedDate": "2026-11-01T18:00:00Z", "status": 2, "type": 0, "bestOf": 1,
          "maps": [{ "mapName": "Mirage", "ourScore": 13, "opponentScore": 4 }] }
        """);

    [Fact]
    public async Task Recording_a_result_returns_the_finished_match_with_a_new_version()
    {
        var (client, teamId, match) = await ScheduledMatchAsync();

        var response = await client.PutAsync($"/api/Matches/{teamId}/{match.Id}", Finished(match.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<MatchDto>())!;
        Assert.Equal("Finished", updated.Status);
        Assert.Equal("13 - 4", updated.FinalScore);
        Assert.Equal(match.Version + 1, updated.Version);
    }

    [Fact]
    public async Task A_stale_version_answers_409()
    {
        var (client, teamId, match) = await ScheduledMatchAsync();
        (await client.PutAsync($"/api/Matches/{teamId}/{match.Id}", Finished(match.Version))).EnsureSuccessStatusCode();

        var response = await client.PutAsync($"/api/Matches/{teamId}/{match.Id}", Finished(match.Version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_invalid_edit_answers_400_before_reaching_the_service()
    {
        var (client, teamId, match) = await ScheduledMatchAsync();

        // Status 1 was "Live", which no longer exists.
        var live = await client.PutAsync($"/api/Matches/{teamId}/{match.Id}", Json($$"""
            { "version": {{match.Version}}, "opponentName": "Kyoto Kitsune", "scheduledDate": "2026-11-01T18:00:00Z", "status": 1, "type": 0 }
            """));
        var scoredScheduled = await client.PutAsync($"/api/Matches/{teamId}/{match.Id}", Json($$"""
            { "version": {{match.Version}}, "opponentName": "Kyoto Kitsune", "scheduledDate": "2026-11-01T18:00:00Z", "status": 0, "type": 0, "ourScore": 2 }
            """));

        Assert.Equal(HttpStatusCode.BadRequest, live.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, scoredScheduled.StatusCode);
    }

    [Fact]
    public async Task A_missing_match_answers_404()
    {
        var (client, teamId, _) = await ScheduledMatchAsync();

        var response = await client.PutAsync($"/api/Matches/{teamId}/999999", Finished(0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
