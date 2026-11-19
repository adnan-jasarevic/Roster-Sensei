using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RosterSensei.Application.Interfaces;

namespace RosterSensei.Infrastructure.Steam;

// Names and avatars from ISteamUser/GetPlayerSummaries. Registered as a singleton so its cache is shared;
// HttpClients come from IHttpClientFactory per call, so connections are pooled and DNS changes are seen.
public sealed class SteamWebApiProfileProvider : ISteamProfileProvider, IDisposable
{
    public const string HttpClientName = "steam-web-api";

    private const int MaxIdsPerRequest = 100; // Steam's limit for GetPlayerSummaries.
    private const int MaxPersonaNameLength = 64;

    public static readonly TimeSpan FoundLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan MissingLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan PauseAfterFailure = TimeSpan.FromMinutes(1);

    // Size-limited so the cache can't grow without bound however many accounts get linked.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<SteamOptions> _options;
    private readonly ILogger<SteamWebApiProfileProvider> _logger;
    private readonly TimeProvider _time;

    // While Steam is failing, rosters skip the lookup instead of each waiting for the timeout.
    private long _pausedUntilTicks;

    public SteamWebApiProfileProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<SteamOptions> options,
        ILogger<SteamWebApiProfileProvider> logger,
        TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _time = time;
    }

    public async Task<IReadOnlyDictionary<string, SteamProfile>> GetProfilesAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, SteamProfile>();
        var apiKey = _options.CurrentValue.WebApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || steamIds.Count == 0)
            return result;

        var missing = new List<string>();
        foreach (var id in steamIds.Distinct())
        {
            if (_cache.TryGetValue(id, out CachedProfile? cached))
            {
                if (cached!.Profile != null)
                    result[id] = cached.Profile;
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count == 0 || _time.GetUtcNow().UtcTicks < Interlocked.Read(ref _pausedUntilTicks))
            return result;

        foreach (var chunk in missing.Chunk(MaxIdsPerRequest))
        {
            var fetched = await FetchAsync(apiKey, chunk, cancellationToken);
            if (fetched == null)
                break;

            foreach (var id in chunk)
            {
                fetched.TryGetValue(id, out var profile);
                _cache.Set(id, new CachedProfile(profile), new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = profile == null ? MissingLifetime : FoundLifetime
                });
                if (profile != null)
                    result[id] = profile;
            }
        }

        return result;
    }

    // Null when Steam couldn't be asked; the caller then shows rosters without names until it recovers.
    private async Task<Dictionary<string, SteamProfile>?> FetchAsync(string apiKey, string[] ids, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            // .NET redacts query strings in HttpClient logs, so the key in the URL doesn't reach the logs.
            var url = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/"
                + $"?key={Uri.EscapeDataString(apiKey)}&steamids={string.Join(',', ids)}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 401/403 usually mean a wrong or revoked key.
                _logger.LogWarning("Steam Web API returned HTTP {StatusCode} for player summaries.", (int)response.StatusCode);
                Pause();
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<SummariesResponse>(cancellationToken);
            var profiles = new Dictionary<string, SteamProfile>();
            foreach (var player in body?.Response?.Players ?? [])
            {
                if (player.SteamId == null || !ids.Contains(player.SteamId) || string.IsNullOrWhiteSpace(player.PersonaName))
                    continue;

                var name = player.PersonaName.Trim();
                if (name.Length > MaxPersonaNameLength)
                    name = name[..MaxPersonaNameLength];
                profiles[player.SteamId] = new SteamProfile(name, SafeAvatarUrl(player.AvatarMedium ?? player.Avatar));
            }
            return profiles;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The roster request was abandoned; not Steam's fault, so no pause.
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Steam Web API player summaries failed.");
            Pause();
            return null;
        }
    }

    private void Pause() =>
        Interlocked.Exchange(ref _pausedUntilTicks, (_time.GetUtcNow() + PauseAfterFailure).UtcTicks);

    // Only https images from Steam's own CDN reach the browser.
    public static string? SafeAvatarUrl(string? url)
    {
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var host = uri.Host;
        return host == "steamstatic.com" || host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("steamcdn-a.akamaihd.net", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }

    public void Dispose() => _cache.Dispose();

    private sealed record CachedProfile(SteamProfile? Profile);

    private sealed class SummariesResponse
    {
        [JsonPropertyName("response")] public SummariesBody? Response { get; set; }
    }

    private sealed class SummariesBody
    {
        [JsonPropertyName("players")] public List<Player>? Players { get; set; }
    }

    private sealed class Player
    {
        [JsonPropertyName("steamid")] public string? SteamId { get; set; }
        [JsonPropertyName("personaname")] public string? PersonaName { get; set; }
        [JsonPropertyName("avatar")] public string? Avatar { get; set; }
        [JsonPropertyName("avatarmedium")] public string? AvatarMedium { get; set; }
    }
}
