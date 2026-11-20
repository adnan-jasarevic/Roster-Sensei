using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
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

public class SteamCommunityProfileProviderTests : IDisposable
{
    private const string Gabe = "76561197960287930";
    private const string Ghost = "76561198000000000";
    private const string Avatar = "https://avatars.fastly.steamstatic.com/abc_medium.jpg";

    private readonly FakeTime _time = new();
    private readonly FakeHttpHandler _http;
    private readonly SteamCommunityProfileProvider _provider;
    private Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public SteamCommunityProfileProviderTests()
    {
        _respond = request => Profile(request);
        _http = new FakeHttpHandler((request, _) => _respond(request));
        _provider = new SteamCommunityProfileProvider(
            new FakeHttpClientFactory(_http), NullLogger<SteamCommunityProfileProvider>.Instance, _time);
    }

    public void Dispose() => _provider.Dispose();

    private static string IdOf(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.Split('/')[2];

    // What steamcommunity.com/profiles/<id>/?xml=1 answers: Steam knows Gabe and nobody else.
    private static HttpResponseMessage Profile(HttpRequestMessage request, string avatar = Avatar, string name = "Rabscuttle")
    {
        var id = IdOf(request);
        return FakeHttpHandler.Text(id == Gabe
            ? $"""
              <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
              <profile>
                <steamID64>{id}</steamID64>
                <steamID><![CDATA[{name}]]></steamID>
                <onlineState>offline</onlineState>
                <privacyState>public</privacyState>
                <avatarIcon><![CDATA[https://avatars.fastly.steamstatic.com/abc.jpg]]></avatarIcon>
                <avatarMedium><![CDATA[{avatar}]]></avatarMedium>
                <avatarFull><![CDATA[https://avatars.fastly.steamstatic.com/abc_full.jpg]]></avatarFull>
              </profile>
              """
            : """
              <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
              <response><error><![CDATA[The specified profile could not be found.]]></error></response>
              """);
    }

    [Fact]
    public async Task Reads_the_name_and_avatar_from_the_public_profile_without_a_key()
    {
        var profiles = await _provider.GetProfilesAsync([Gabe, Ghost]);

        Assert.Equal("Rabscuttle", profiles[Gabe].PersonaName);
        Assert.Equal(Avatar, profiles[Gabe].AvatarUrl);
        Assert.False(profiles.ContainsKey(Ghost));
        Assert.Equal(2, _http.Requests.Count);
        Assert.Contains(_http.Requests, r => r.Request.RequestUri!.ToString() == $"https://steamcommunity.com/profiles/{Gabe}/?xml=1");
        Assert.DoesNotContain(_http.Requests, r => r.Request.RequestUri!.Query.Contains("key="));
    }

    [Fact]
    public async Task Found_and_unknown_accounts_are_cached()
    {
        await _provider.GetProfilesAsync([Gabe, Ghost]);
        var again = await _provider.GetProfilesAsync([Gabe, Ghost]);

        Assert.Equal("Rabscuttle", again[Gabe].PersonaName);
        Assert.Equal(2, _http.Requests.Count);
    }

    [Fact]
    public async Task Rosters_loading_at_the_same_time_share_one_lookup_per_account()
    {
        var gate = new TaskCompletionSource();
        _respond = request => { gate.Task.Wait(); return Profile(request); };

        var loads = Enumerable.Range(0, 5).Select(_ => _provider.GetProfilesAsync([Gabe])).ToList();
        await Task.Delay(100);
        gate.SetResult();
        var results = await Task.WhenAll(loads);

        Assert.All(results, r => Assert.Equal("Rabscuttle", r[Gabe].PersonaName));
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Being_rate_limited_pauses_lookups_for_five_minutes()
    {
        _respond = _ => FakeHttpHandler.Text("", (HttpStatusCode)429);
        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));

        _respond = request => Profile(request);
        _time.Advance(SteamCommunityProfileProvider.PauseAfterFailure + TimeSpan.FromSeconds(1));
        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));
        Assert.Single(_http.Requests);

        _time.Advance(SteamCommunityProfileProvider.PauseAfterRateLimit);
        Assert.Equal("Rabscuttle", (await _provider.GetProfilesAsync([Gabe]))[Gabe].PersonaName);
    }

    [Fact]
    public async Task After_a_failure_Steam_is_left_alone_for_a_minute()
    {
        _respond = _ => FakeHttpHandler.Text("", HttpStatusCode.ServiceUnavailable);
        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));

        _respond = request => Profile(request);
        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));
        Assert.Single(_http.Requests);

        _time.Advance(SteamCommunityProfileProvider.PauseAfterFailure + TimeSpan.FromSeconds(1));
        Assert.Equal("Rabscuttle", (await _provider.GetProfilesAsync([Gabe]))[Gabe].PersonaName);
    }

    [Theory]
    [InlineData("<html><body>Steam is down for maintenance</body></html>")]
    [InlineData("not xml at all")]
    [InlineData("<profile><steamID64>76561198000000001</steamID64><steamID>someone else</steamID></profile>")]
    public async Task Garbage_or_someone_elses_profile_is_not_used(string body)
    {
        _respond = _ => FakeHttpHandler.Text(body);

        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));
    }

    [Fact]
    public async Task XML_with_a_DTD_is_refused()
    {
        _respond = _ => FakeHttpHandler.Text("""
            <?xml version="1.0"?>
            <!DOCTYPE profile [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <profile><steamID64>76561197960287930</steamID64><steamID>&xxe;</steamID></profile>
            """);

        Assert.Empty(await _provider.GetProfilesAsync([Gabe]));
    }

    [Fact]
    public async Task Very_long_names_are_cut()
    {
        _respond = request => Profile(request, name: new string('x', 200));

        Assert.Equal(64, (await _provider.GetProfilesAsync([Gabe]))[Gabe].PersonaName.Length);
    }

    [Fact]
    public async Task A_cancelled_roster_request_still_fills_the_cache()
    {
        var gate = new TaskCompletionSource();
        _respond = request => { gate.Task.Wait(); return Profile(request); };
        using var cancel = new CancellationTokenSource();

        var load = _provider.GetProfilesAsync([Gabe], cancel.Token);
        await Task.Delay(50);
        cancel.Cancel();
        Assert.Empty(await load);

        gate.SetResult();
        await Task.Delay(100);
        _respond = _ => throw new InvalidOperationException("should be cached");
        Assert.Equal("Rabscuttle", (await _provider.GetProfilesAsync([Gabe]))[Gabe].PersonaName);
    }

    [Fact]
    public async Task Avatars_from_anywhere_but_Steam_are_dropped()
    {
        _respond = request => Profile(request, avatar: "https://evil.example/tracker.png");

        var profiles = await _provider.GetProfilesAsync([Gabe]);

        Assert.Equal("Rabscuttle", profiles[Gabe].PersonaName);
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
        Assert.Equal(kept, SteamCommunityProfileProvider.SafeAvatarUrl(url) != null);
    }

    [Fact]
    public async Task One_roster_load_looks_up_at_most_25_accounts_and_never_more_than_4_at_once()
    {
        var running = 0;
        var peak = 0;
        _respond = request =>
        {
            var now = Interlocked.Increment(ref running);
            lock (_http) peak = Math.Max(peak, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref running);
            return Profile(request);
        };
        var ids = Enumerable.Range(1, 40).Select(i => (76561198000000000L + i).ToString()).ToList();

        await _provider.GetProfilesAsync(ids);

        Assert.Equal(SteamCommunityProfileProvider.MaxLookupsPerCall, _http.Requests.Count);
        Assert.InRange(peak, 1, SteamCommunityProfileProvider.MaxConcurrentLookups);
    }
}
