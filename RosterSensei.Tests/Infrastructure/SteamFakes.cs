using System.Net;
using RosterSensei.Application.Interfaces;

namespace RosterSensei.Tests.Infrastructure;

// Stands in for Steam's OpenID check: answers with SteamIdToReturn, or null as if Steam said is_valid:false.
public sealed class FakeSteamOpenIdVerifier : ISteamOpenIdVerifier
{
    public string? SteamIdToReturn { get; set; } = "76561197960287930";
    public int VerifyCalls;

    public string BuildSignInUrl(string returnTo, string realm) =>
        $"https://steamcommunity.com/openid/login?openid.return_to={Uri.EscapeDataString(returnTo)}&openid.realm={Uri.EscapeDataString(realm)}";

    public Task<string?> VerifyAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref VerifyCalls);
        return Task.FromResult(SteamIdToReturn);
    }
}

public sealed class FakeSteamProfiles : ISteamProfileProvider
{
    public Dictionary<string, SteamProfile> Profiles { get; } = new();

    public Task<IReadOnlyDictionary<string, SteamProfile>> GetProfilesAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, SteamProfile>>(
            Profiles.Where(p => steamIds.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
}

public sealed class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

// Answers HTTP requests with a function and records them.
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public FakeHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) => _respond = respond;

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
            Requests.Add((request, body));
        return _respond(request, body);
    }
}

public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
