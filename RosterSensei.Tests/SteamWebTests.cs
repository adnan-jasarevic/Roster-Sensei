using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RosterSensei.Infrastructure.Steam;
using RosterSensei.Tests.Infrastructure;

namespace RosterSensei.Tests;

public class SteamOpenIdVerifierTests
{
    private const string SteamId64 = "76561197960287930";

    private static Dictionary<string, string> GenuineReply() => new()
    {
        ["openid.ns"] = "http://specs.openid.net/auth/2.0",
        ["openid.mode"] = "id_res",
        ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
        ["openid.claimed_id"] = $"https://steamcommunity.com/openid/id/{SteamId64}",
        ["openid.identity"] = $"https://steamcommunity.com/openid/id/{SteamId64}",
        ["openid.return_to"] = "http://localhost:5173/account?rs_state=AB",
        ["openid.response_nonce"] = "2026-11-19T12:00:00Zabc",
        ["openid.assoc_handle"] = "1234567890",
        ["openid.signed"] = "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
        ["openid.sig"] = "c2lnbmF0dXJl"
    };

    private static (SteamOpenIdVerifier Verifier, FakeHttpHandler Http) Create(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        var http = new FakeHttpHandler(respond);
        return (new SteamOpenIdVerifier(new FakeHttpClientFactory(http), NullLogger<SteamOpenIdVerifier>.Instance), http);
    }

    [Fact]
    public void The_sign_in_URL_asks_Steam_to_pick_the_account_and_return_to_us()
    {
        var (verifier, _) = Create((_, _) => throw new InvalidOperationException());

        var url = verifier.BuildSignInUrl("http://localhost:5173/account?rs_state=AB", "http://localhost:5173");

        Assert.StartsWith("https://steamcommunity.com/openid/login?", url);
        Assert.Contains("openid.mode=checkid_setup", url);
        Assert.Contains("openid.return_to=http%3A%2F%2Flocalhost%3A5173%2Faccount%3Frs_state%3DAB", url);
        Assert.Contains("openid.realm=http%3A%2F%2Flocalhost%3A5173", url);
        Assert.Contains("openid.claimed_id=http%3A%2F%2Fspecs.openid.net%2Fauth%2F2.0%2Fidentifier_select", url);
    }

    [Fact]
    public void A_genuine_looking_reply_names_the_SteamID64()
    {
        Assert.Equal(SteamId64, SteamOpenIdVerifier.ReadClaimedSteamId(GenuineReply()));
    }

    [Theory]
    [InlineData("openid.op_endpoint", "https://evil.example/openid/login")]
    [InlineData("openid.claimed_id", "https://evil.example/openid/id/76561197960287930")]
    [InlineData("openid.claimed_id", "https://steamcommunity.com/openid/id/76561198000000000")]
    [InlineData("openid.identity", "https://steamcommunity.com/openid/id/76561198000000000")]
    [InlineData("openid.signed", "signed,op_endpoint,identity,return_to,response_nonce,assoc_handle")]
    [InlineData("openid.mode", "cancel")]
    [InlineData("openid.ns", "http://openid.net/signon/1.1")]
    [InlineData("openid.sig", "")]
    public void A_reply_Steam_did_not_sign_as_expected_is_refused(string key, string value)
    {
        var reply = GenuineReply();
        reply[key] = value;

        Assert.Null(SteamOpenIdVerifier.ReadClaimedSteamId(reply));
    }

    [Fact]
    public async Task A_reply_Steam_confirms_is_accepted()
    {
        var (verifier, http) = Create((_, _) => FakeHttpHandler.Text("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n"));

        Assert.Equal(SteamId64, await verifier.VerifyAsync(GenuineReply()));

        var (request, body) = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SteamOpenIdVerifier.Endpoint, request.RequestUri!.ToString());
        Assert.Contains("openid.mode=check_authentication", body);
        Assert.Contains("openid.sig=c2lnbmF0dXJl", body);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "ns:http://specs.openid.net/auth/2.0\nis_valid:false\n")]
    [InlineData(HttpStatusCode.OK, "is_valid:truex\n")]
    [InlineData(HttpStatusCode.InternalServerError, "is_valid:true\n")]
    public async Task A_reply_Steam_does_not_confirm_is_refused(HttpStatusCode status, string body)
    {
        var (verifier, _) = Create((_, _) => FakeHttpHandler.Text(body, status));

        Assert.Null(await verifier.VerifyAsync(GenuineReply()));
    }

    [Fact]
    public async Task Steam_being_unreachable_is_a_refusal_not_a_crash()
    {
        var (verifier, _) = Create((_, _) => throw new HttpRequestException("down"));

        Assert.Null(await verifier.VerifyAsync(GenuineReply()));
    }

    [Fact]
    public async Task A_malformed_reply_is_never_sent_to_Steam()
    {
        var (verifier, http) = Create((_, _) => FakeHttpHandler.Text("is_valid:true"));
        var reply = GenuineReply();
        reply.Remove("openid.assoc_handle");

        Assert.Null(await verifier.VerifyAsync(reply));
        Assert.Empty(http.Requests);
    }
}

public class SteamWebApiProfileProviderTests : IDisposable
{
    private const string Gabe = "76561197960287930";
    private const string Ghost = "76561198000000000";

    private readonly FakeTime _time = new();
    private readonly SteamOptions _options = new() { WebApiKey = "test-key" };
    private readonly FakeHttpHandler _http;
    private readonly SteamWebApiProfileProvider _provider;
    private Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public SteamWebApiProfileProviderTests()
    {
        _respond = request => Summaries(request);
        _http = new FakeHttpHandler((request, _) => _respond(request));
        _provider = new SteamWebApiProfileProvider(
            new FakeHttpClientFactory(_http), new StaticOptions(_options), NullLogger<SteamWebApiProfileProvider>.Instance, _time);
    }

    public void Dispose() => _provider.Dispose();

    // Steam knows Gabe and nobody else.
    private static HttpResponseMessage Summaries(HttpRequestMessage request, string avatar = "https://avatars.steamstatic.com/abc_medium.jpg")
    {
        var ids = request.RequestUri!.Query.Split("steamids=")[1].Split(',');
        var players = ids.Where(id => id == Gabe)
            .Select(id => new { steamid = id, personaname = "gaben", avatarmedium = avatar });
        return FakeHttpHandler.Text(JsonSerializer.Serialize(new { response = new { players } }));
    }

    [Fact]
    public async Task Returns_names_and_avatars_from_one_request()
    {
        var profiles = await _provider.GetProfilesAsync([Gabe, Ghost]);

        Assert.Equal("gaben", profiles[Gabe].PersonaName);
        Assert.Equal("https://avatars.steamstatic.com/abc_medium.jpg", profiles[Gabe].AvatarUrl);
        Assert.False(profiles.ContainsKey(Ghost));
        var (request, _) = Assert.Single(_http.Requests);
        Assert.StartsWith("https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=test-key&steamids=", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Without_an_API_key_Steam_is_never_called()
    {
        _options.WebApiKey = "";

        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Found_and_unknown_accounts_are_cached()
    {
        await _provider.GetProfilesAsync([Gabe, Ghost]);
        var again = await _provider.GetProfilesAsync([Gabe, Ghost]);

        Assert.Equal("gaben", again[Gabe].PersonaName);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task After_a_failure_Steam_is_left_alone_for_a_minute()
    {
        _respond = _ => FakeHttpHandler.Text("", HttpStatusCode.Forbidden);
        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));

        _respond = request => Summaries(request);
        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));
        Assert.Single(_http.Requests);

        _time.Advance(SteamWebApiProfileProvider.PauseAfterFailure + TimeSpan.FromSeconds(1));
        Assert.Equal("gaben", (await _provider.GetProfilesAsync([Gabe]))[Gabe].PersonaName);
    }

    [Fact]
    public async Task Garbage_from_Steam_is_a_failure_not_a_crash()
    {
        _respond = _ => FakeHttpHandler.Text("<html>maintenance</html>");

        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));
    }

    [Fact]
    public async Task A_cancelled_roster_request_does_not_pause_lookups()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        _respond = _ => throw new TaskCanceledException();

        Assert.Empty(await _provider.GetProfilesAsync([Gabe], cancelled.Token));

        _respond = request => Summaries(request);
        Assert.Equal("gaben", (await _provider.GetProfilesAsync([Gabe]))[Gabe].PersonaName);
    }

    [Fact]
    public async Task Avatars_from_anywhere_but_Steam_are_dropped()
    {
        _respond = request => Summaries(request, avatar: "https://evil.example/tracker.png");

        var profiles = await _provider.GetProfilesAsync([Gabe]);

        Assert.Equal("gaben", profiles[Gabe].PersonaName);
        Assert.Null(profiles[Gabe].AvatarUrl);
    }

    [Theory]
    [InlineData("https://avatars.steamstatic.com/a.jpg", true)]
    [InlineData("https://avatars.fastly.steamstatic.com/a.jpg", true)]
    [InlineData("https://steamcdn-a.akamaihd.net/a.jpg", true)]
    [InlineData("http://avatars.steamstatic.com/a.jpg", false)]
    [InlineData("https://steamstatic.com.evil.example/a.jpg", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Only_https_Steam_CDN_avatars_are_kept(string url, bool kept)
    {
        Assert.Equal(kept, SteamWebApiProfileProvider.SafeAvatarUrl(url) != null);
    }

    [Fact]
    public async Task Large_rosters_are_split_into_requests_of_100()
    {
        var ids = Enumerable.Range(1, 150).Select(i => (76561198000000000L + i).ToString()).ToList();

        await _provider.GetProfilesAsync(ids);

        Assert.Equal(2, _http.Requests.Count);
    }

    private sealed class StaticOptions(SteamOptions value) : IOptionsMonitor<SteamOptions>
    {
        public SteamOptions CurrentValue => value;
        public SteamOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SteamOptions, string?> listener) => null;
    }
}
