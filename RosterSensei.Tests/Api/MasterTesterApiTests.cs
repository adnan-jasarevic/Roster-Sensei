using RosterSensei.Application.DTOs;
using RosterSensei.Application.Validators;
using RosterSensei.Domain.Entities;
using System.Net;
using System.Net.Http.Json;

namespace RosterSensei.Tests.Api;

// The master tester tools: hidden from everyone but the configured testers, and the data they
// generate passes the same validation as data entered by hand.
public class MasterTesterApiTests : IDisposable
{
    private readonly ApiFactory _api = new(new Dictionary<string, string>
    {
        ["MasterTester:Enabled"] = "true",
        ["MasterTester:Emails:0"] = ApiFactory.EmailOf("tester"),
    });

    public void Dispose() => _api.Dispose();

    private static readonly SeedSectionDto Add = new() { Mode = "add", TimeZone = "Europe/Sarajevo" };
    private static readonly SeedSectionDto Replace = new() { Mode = "replace", TimeZone = "Europe/Sarajevo" };

    private static async Task<int> CreateFullTeamAsync(HttpClient tester)
    {
        var response = await tester.PostAsJsonAsync("/api/master-tester/teams", new SeedTeamDto { TimeZone = "Europe/Sarajevo" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SeedResultDto>())!.TeamId!.Value;
    }

    [Fact]
    public async Task The_tools_answer_404_to_anyone_who_is_not_a_master_tester()
    {
        var alice = await _api.SignedInClientAsync("alice");

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync("/api/master-tester")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync("/api/master-tester/teams", new SeedTeamDto())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync("/api/master-tester/invites", Add)).StatusCode);
    }

    [Fact]
    public async Task The_tools_answer_404_to_the_tester_when_they_are_switched_off()
    {
        using var api = new ApiFactory(new Dictionary<string, string>
        {
            ["MasterTester:Enabled"] = "false",
            ["MasterTester:Emails:0"] = ApiFactory.EmailOf("tester"),
        });
        var tester = await api.SignedInClientAsync("tester");

        Assert.Equal(HttpStatusCode.NotFound, (await tester.GetAsync("/api/master-tester")).StatusCode);
    }

    [Fact]
    public async Task The_tools_need_a_signed_in_user()
    {
        var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/master-tester")).StatusCode);
    }

    [Fact]
    public async Task A_full_team_has_players_matches_events_off_days_and_ratings()
    {
        var tester = await _api.SignedInClientAsync("tester");
        Assert.Equal(HttpStatusCode.OK, (await tester.GetAsync("/api/master-tester")).StatusCode);

        var teamId = await CreateFullTeamAsync(tester);

        var team = Assert.Single((await tester.GetFromJsonAsync<List<TeamDto>>("/api/Teams/my-teams"))!);
        Assert.True(team.IsOwner);
        Assert.Equal("Captain", team.CurrentUserRole);

        var members = (await tester.GetFromJsonAsync<List<TeamMemberDto>>($"/api/Teams/{teamId}/members"))!;
        Assert.Equal(7, members.Count);
        Assert.Single(members, m => m.Role == "Officer");
        Assert.All(members.Where(m => !m.IsOwner), m => Assert.NotNull(m.SteamId));

        var matches = (await tester.GetFromJsonAsync<List<MatchDto>>($"/api/Matches/{teamId}"))!;
        Assert.Equal(14, matches.Count(m => m.Status == "Finished"));
        Assert.Equal(5, matches.Count(m => m.Status == "Scheduled"));

        var from = DateTime.UtcNow.AddDays(-70).ToString("o");
        var to = DateTime.UtcNow.AddDays(70).ToString("o");
        var events = (await tester.GetFromJsonAsync<List<TeamEventDto>>($"/api/teams/{teamId}/events?from={from}&to={to}"))!;
        Assert.NotEmpty(events);
        var offDays = (await tester.GetFromJsonAsync<List<TeamOffDayDto>>($"/api/teams/{teamId}/off-days?from={from}&to={to}"))!;
        Assert.Equal(6, offDays.Count);

        var mapPool = (await tester.GetFromJsonAsync<MapPoolDto>($"/api/teams/{teamId}/map-pool"))!;
        Assert.Equal(7 * MapPool.Maps.Count * 2, mapPool.Ratings.Count);
    }

    [Fact]
    public async Task Every_generated_match_passes_the_validation_of_a_hand_entered_one()
    {
        var tester = await _api.SignedInClientAsync("tester");
        var teamId = await CreateFullTeamAsync(tester);
        // A second batch, for more random scores.
        (await tester.PostAsJsonAsync($"/api/master-tester/teams/{teamId}/matches", Add)).EnsureSuccessStatusCode();

        var validator = new CreateMatchDtoValidator();
        foreach (var match in (await tester.GetFromJsonAsync<List<MatchDto>>($"/api/Matches/{teamId}"))!)
        {
            var asEntered = new CreateMatchDto
            {
                OpponentName = match.OpponentName,
                Tournament = match.Tournament,
                ScheduledDate = match.ScheduledDate,
                PlayedDate = match.PlayedDate,
                Status = Enum.Parse<MatchStatus>(match.Status),
                Type = Enum.Parse<MatchKind>(match.Type),
                BestOf = match.BestOf,
                Maps = match.Maps,
            };
            var result = validator.Validate(asEntered);
            Assert.True(result.IsValid, $"{match.FinalScore} vs {match.OpponentName}: {result}");
        }
    }

    [Fact]
    public async Task Replace_clears_a_section_before_filling_it_and_add_keeps_it()
    {
        var tester = await _api.SignedInClientAsync("tester");
        var teamId = await CreateFullTeamAsync(tester);

        (await tester.PostAsJsonAsync($"/api/master-tester/teams/{teamId}/matches", Add)).EnsureSuccessStatusCode();
        Assert.Equal(38, (await tester.GetFromJsonAsync<List<MatchDto>>($"/api/Matches/{teamId}"))!.Count);

        (await tester.PostAsJsonAsync($"/api/master-tester/teams/{teamId}/matches", Replace)).EnsureSuccessStatusCode();
        Assert.Equal(19, (await tester.GetFromJsonAsync<List<MatchDto>>($"/api/Matches/{teamId}"))!.Count);

        // The Owner always stays on the roster.
        (await tester.PostAsJsonAsync($"/api/master-tester/teams/{teamId}/roster", Replace)).EnsureSuccessStatusCode();
        var members = (await tester.GetFromJsonAsync<List<TeamMemberDto>>($"/api/Teams/{teamId}/members"))!;
        Assert.Equal(7, members.Count);
        Assert.Contains(members, m => m.IsOwner && m.Email == ApiFactory.EmailOf("tester"));
    }

    [Fact]
    public async Task The_tester_cannot_fill_a_team_they_are_not_in()
    {
        var tester = await _api.SignedInClientAsync("tester");
        var alice = await _api.SignedInClientAsync("alice");
        var created = await alice.PostAsJsonAsync("/api/Teams", new CreateTeamDto { Name = "Alpha Team" });
        created.EnsureSuccessStatusCode();
        var aliceTeamId = (await created.Content.ReadFromJsonAsync<TeamDto>())!.Id;

        var response = await tester.PostAsJsonAsync($"/api/master-tester/teams/{aliceTeamId}/matches", Add);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_section_or_mode_is_refused()
    {
        var tester = await _api.SignedInClientAsync("tester");
        var teamId = await CreateFullTeamAsync(tester);

        Assert.Equal(HttpStatusCode.NotFound, (await tester.PostAsJsonAsync($"/api/master-tester/teams/{teamId}/trophies", Add)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await tester.PostAsJsonAsync($"/api/master-tester/teams/{teamId}/matches", new SeedSectionDto { Mode = "wipe" })).StatusCode);
    }

    [Fact]
    public async Task Invites_arrive_in_the_testers_bell()
    {
        var tester = await _api.SignedInClientAsync("tester");

        (await tester.PostAsJsonAsync("/api/master-tester/invites", Add)).EnsureSuccessStatusCode();
        Assert.Equal(2, (await tester.GetFromJsonAsync<List<TeamInviteDto>>("/api/Teams/my-invites"))!.Count);

        (await tester.PostAsJsonAsync("/api/master-tester/invites", Replace)).EnsureSuccessStatusCode();
        Assert.Equal(2, (await tester.GetFromJsonAsync<List<TeamInviteDto>>("/api/Teams/my-invites"))!.Count);
    }
}
