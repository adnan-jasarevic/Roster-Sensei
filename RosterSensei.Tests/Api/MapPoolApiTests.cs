using RosterSensei.Application.DTOs;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace RosterSensei.Tests.Api;

// The Map Pool endpoints through HTTP: routing, the validation filter, and that a member can only
// ever write their own column.
public class MapPoolApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<(HttpClient Alice, HttpClient Bob, int TeamId)> TeamOfTwoAsync()
    {
        var alice = await _api.SignedInClientAsync("alice");
        var bob = await _api.SignedInClientAsync("bob");

        var created = await alice.PostAsJsonAsync("/api/Teams", new CreateTeamDto { Name = "Alpha Team" });
        created.EnsureSuccessStatusCode();
        var teamId = (await created.Content.ReadFromJsonAsync<TeamDto>())!.Id;

        (await alice.PostAsJsonAsync($"/api/Teams/{teamId}/members", new AddTeamMemberDto { UserEmail = ApiFactory.EmailOf("bob") })).EnsureSuccessStatusCode();
        var invite = Assert.Single((await bob.GetFromJsonAsync<List<TeamInviteDto>>("/api/Teams/my-invites"))!);
        (await bob.PostAsJsonAsync($"/api/Teams/invites/{invite.InviteId}/respond", true)).EnsureSuccessStatusCode();

        return (alice, bob, teamId);
    }

    // Sent as raw JSON so the number arrives exactly as a browser would send it.
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_rating_saved_by_one_member_is_read_by_the_other()
    {
        var (alice, bob, teamId) = await TeamOfTwoAsync();

        var save = await bob.PutAsync($"/api/teams/{teamId}/map-pool/CT/Mirage", Json("{\"rating\":6.5}"));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var pool = await alice.GetFromJsonAsync<MapPoolDto>($"/api/teams/{teamId}/map-pool");
        Assert.Equal(2, pool!.Players.Count);
        var rating = Assert.Single(pool.Ratings);
        Assert.Equal("CT", rating.Side);
        Assert.Equal("Mirage", rating.MapName);
        Assert.Equal(6.5m, rating.Rating);
        Assert.Equal(pool.Players.Single(p => p.Username == "bob").UserId, rating.UserId);
    }

    [Fact]
    public async Task A_user_id_in_the_body_is_ignored_and_the_rating_stays_the_callers_own()
    {
        var (alice, bob, teamId) = await TeamOfTwoAsync();
        var pool = await alice.GetFromJsonAsync<MapPoolDto>($"/api/teams/{teamId}/map-pool");
        var aliceId = pool!.Players.Single(p => p.Username == "alice").UserId;

        (await bob.PutAsync($"/api/teams/{teamId}/map-pool/T/Nuke", Json($"{{\"rating\":2,\"userId\":\"{aliceId}\"}}"))).EnsureSuccessStatusCode();

        pool = await alice.GetFromJsonAsync<MapPoolDto>($"/api/teams/{teamId}/map-pool");
        Assert.NotEqual(aliceId, Assert.Single(pool!.Ratings).UserId);
    }

    [Theory]
    [InlineData("{\"rating\":6.55}")]
    [InlineData("{\"rating\":11}")]
    [InlineData("{\"rating\":0.5}")]
    [InlineData("{}")]
    public async Task An_invalid_rating_returns_400(string body)
    {
        var (_, bob, teamId) = await TeamOfTwoAsync();

        var response = await bob.PutAsync($"/api/teams/{teamId}/map-pool/T/Mirage", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_map_outside_the_pool_returns_404()
    {
        var (_, bob, teamId) = await TeamOfTwoAsync();

        var response = await bob.PutAsync($"/api/teams/{teamId}/map-pool/T/Cobblestone", Json("{\"rating\":5}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Someone_outside_the_team_gets_403()
    {
        var (_, _, teamId) = await TeamOfTwoAsync();
        var carol = await _api.SignedInClientAsync("carol");

        Assert.Equal(HttpStatusCode.Forbidden, (await carol.GetAsync($"/api/teams/{teamId}/map-pool")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.PutAsync($"/api/teams/{teamId}/map-pool/T/Mirage", Json("{\"rating\":5}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carol.DeleteAsync($"/api/teams/{teamId}/map-pool/T/Mirage")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_rating_clears_it()
    {
        var (alice, _, teamId) = await TeamOfTwoAsync();
        (await alice.PutAsync($"/api/teams/{teamId}/map-pool/T/Mirage", Json("{\"rating\":5}"))).EnsureSuccessStatusCode();

        var response = await alice.DeleteAsync($"/api/teams/{teamId}/map-pool/T/Mirage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<MapPoolDto>($"/api/teams/{teamId}/map-pool"))!.Ratings);
    }

    // Double submits or two tabs saving a first rating at once must leave one row, not fail or duplicate.
    [Fact]
    public async Task Simultaneous_saves_of_the_same_rating_leave_one_rating()
    {
        var (alice, _, teamId) = await TeamOfTwoAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            alice.PutAsync($"/api/teams/{teamId}/map-pool/CT/Anubis", Json("{\"rating\":7}"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var rating = Assert.Single((await alice.GetFromJsonAsync<MapPoolDto>($"/api/teams/{teamId}/map-pool"))!.Ratings);
        Assert.Equal(7m, rating.Rating);
    }
}
