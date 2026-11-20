using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests.Api;

// Steam sign-in through HTTP, with Steam's own check faked.
public class SteamApiTests : IDisposable
{
    private const string AccountPage = "http://localhost:5173/account";

    private readonly FakeSteamOpenIdVerifier _steam = new();
    private readonly ApiFactory _api;

    public SteamApiTests()
    {
        _api = new ApiFactory(
            new Dictionary<string, string> { ["Cors:AllowedOrigins:0"] = "http://localhost:5173" },
            services =>
            {
                services.RemoveAll<ISteamOpenIdVerifier>();
                services.AddSingleton<ISteamOpenIdVerifier>(_steam);
            });
    }

    public void Dispose() => _api.Dispose();

    private static async Task<CompleteSteamSignInDto> SignInAtSteamAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/Account/steam/sign-in", new StartSteamSignInDto { ReturnUrl = AccountPage });
        response.EnsureSuccessStatusCode();
        var started = await response.Content.ReadFromJsonAsync<SteamSignInStartedDto>();
        var returnTo = Uri.UnescapeDataString(new Uri(started!.RedirectUrl).Query.Split("openid.return_to=")[1].Split('&')[0]);

        return new CompleteSteamSignInDto
        {
            Parameters = new Dictionary<string, string> { ["openid.mode"] = "id_res", ["openid.return_to"] = returnTo }
        };
    }

    [Fact]
    public async Task Signing_in_through_Steam_links_the_account()
    {
        var alice = await _api.SignedInClientAsync("alice");

        var response = await alice.PostAsJsonAsync("/api/Account/steam/sign-in/complete", await SignInAtSteamAsync(alice));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await alice.GetFromJsonAsync<SteamAccountDto>("/api/Account/steam");
        Assert.Equal(_steam.SteamIdToReturn, account!.SteamId);
        Assert.True(account.Verified);
    }

    [Fact]
    public async Task An_account_verified_by_someone_else_returns_409()
    {
        var alice = await _api.SignedInClientAsync("alice");
        var bob = await _api.SignedInClientAsync("bob");
        (await alice.PostAsJsonAsync("/api/Account/steam/sign-in/complete", await SignInAtSteamAsync(alice))).EnsureSuccessStatusCode();

        var response = await bob.PostAsJsonAsync("/api/Account/steam/sign-in/complete", await SignInAtSteamAsync(bob));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Pasting_a_Steam_ID_is_no_longer_possible()
    {
        var alice = await _api.SignedInClientAsync("alice");

        var response = await alice.PutAsJsonAsync("/api/Account/steam", new { steam = "76561197960287930" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task A_return_URL_outside_the_frontend_is_a_400()
    {
        var alice = await _api.SignedInClientAsync("alice");

        var response = await alice.PostAsJsonAsync("/api/Account/steam/sign-in", new StartSteamSignInDto { ReturnUrl = "https://evil.example/account" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_reply_is_rejected_before_anything_else()
    {
        var alice = await _api.SignedInClientAsync("alice");
        var reply = new CompleteSteamSignInDto
        {
            Parameters = Enumerable.Range(0, 50).ToDictionary(i => $"openid.x{i}", _ => "y")
        };

        var response = await alice.PostAsJsonAsync("/api/Account/steam/sign-in/complete", reply);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _steam.VerifyCalls);
    }

    [Theory]
    [InlineData("/api/Account/steam/sign-in")]
    [InlineData("/api/Account/steam/sign-in/complete")]
    public async Task Sign_in_needs_a_token(string path)
    {
        var response = await _api.CreateClient().PostAsJsonAsync(path, new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
