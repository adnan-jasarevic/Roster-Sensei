using RosterSensei.Application.DTOs;
using System.Net;
using System.Net.Http.Json;

namespace RosterSensei.Tests.Api;

// /api/DemoImport through HTTP: routing, the validation filter and the status codes.
public class DemoImportApiTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private static DemoSummaryDto Demo(int firstHalf = 8) => new()
    {
        ParserVersion = "demoparser2 0.42.3",
        PatchVersion = 14175,
        Fingerprint = new string('f', 64),
        MapName = "de_mirage",
        Rounds = 24,
        Sides = new()
        {
            new DemoSideDto { Score = 17, FirstHalf = firstHalf, SecondHalf = 9, Players = new() { new DemoPlayerDto { SteamId = "76561198212412773", Name = "Maze", Kills = 23, Deaths = 13, Assists = 11, Damage = 2588, HeadshotKills = 13 } } },
            new DemoSideDto { Score = 7, FirstHalf = 4, SecondHalf = 3 }
        }
    };

    private async Task<(HttpClient Client, int TeamId, MatchDto Match)> MatchAsync()
    {
        var client = await _api.SignedInClientAsync("alice");
        var created = await client.PostAsJsonAsync("/api/Teams", new CreateTeamDto { Name = "Alpha Team" });
        var teamId = (await created.Content.ReadFromJsonAsync<TeamDto>())!.Id;
        var added = await client.PostAsJsonAsync($"/api/Matches/{teamId}", new CreateMatchDto { OpponentName = "Friends", ScheduledDate = DateTime.UtcNow });
        added.EnsureSuccessStatusCode();
        return (client, teamId, (await added.Content.ReadFromJsonAsync<MatchDto>())!);
    }

    [Fact]
    public async Task Status_answers_whether_import_is_paused()
    {
        var client = await _api.SignedInClientAsync("alice");

        var status = await client.GetFromJsonAsync<DemoImportStatusDto>("/api/DemoImport/status?parserVersion=demoparser2%200.42.3");

        Assert.False(status!.Paused);
    }

    [Fact]
    public async Task Preview_answers_200_with_what_the_demo_holds()
    {
        var (client, teamId, match) = await MatchAsync();

        var response = await client.PostAsJsonAsync($"/api/DemoImport/{teamId}/{match.Id}/preview", new PreviewDemoImportDto { Demo = Demo() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<DemoImportPreviewDto>())!;
        Assert.Null(preview.OurSide);
        Assert.Equal(107.8m, preview.Sides[0].Players[0].Adr);
    }

    [Fact]
    public async Task A_broken_demo_answers_400_before_reaching_the_service()
    {
        var (client, teamId, match) = await MatchAsync();

        var response = await client.PostAsJsonAsync($"/api/DemoImport/{teamId}/{match.Id}",
            new ApplyDemoImportDto { Demo = Demo(firstHalf: 12), OurSide = 0, MapOrder = 1, Version = match.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Signed_out_requests_answer_401()
    {
        var response = await _api.CreateClient().GetAsync("/api/DemoImport/status?parserVersion=x");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
