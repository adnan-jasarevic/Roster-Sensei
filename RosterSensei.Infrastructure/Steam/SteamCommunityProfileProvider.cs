using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RosterSensei.Application.Interfaces;

namespace RosterSensei.Infrastructure.Steam;

// Names and avatars from public Steam Community profiles (steamcommunity.com/profiles/<id>/?xml=1), which
// need no Steam Web API key. Steam rate-limits these pages, so lookups are cached, shared between requests
// asking for the same ID, limited in how many run at once, and paused for a while after Steam refuses.
// Registered as a singleton so the cache and limits are shared; HttpClients come from IHttpClientFactory per call.
public sealed class SteamCommunityProfileProvider : ISteamProfileProvider, IDisposable
{
    public const string HttpClientName = "steam-community";

    // How many profile pages are fetched at the same time across the whole API, and per roster load.
    // A roster bigger than MaxLookupsPerCall fills in the rest on later loads.
    public const int MaxConcurrentLookups = 4;
    public const int MaxLookupsPerCall = 25;
    private const int MaxPersonaNameLength = 64;

    public static readonly TimeSpan FoundLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan MissingLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan PauseAfterFailure = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan PauseAfterRateLimit = TimeSpan.FromMinutes(5);

    private static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 2_000_000,
        Async = true
    };

    // Size-limited so the cache can't grow without bound however many accounts get linked.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    // One fetch per Steam ID at a time; requests asking for the same ID share it.
    // Lazy, because GetOrAdd can run its factory twice when two requests race; only one Lazy is ever kept.
    private readonly ConcurrentDictionary<string, Lazy<Task<Lookup>>> _inFlight = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrency = new(MaxConcurrentLookups, MaxConcurrentLookups);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SteamCommunityProfileProvider> _logger;
    private readonly TimeProvider _time;

    // While Steam is failing or rate-limiting us, rosters skip the lookups instead of each waiting for the timeout.
    private long _pausedUntilTicks;

    public SteamCommunityProfileProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<SteamCommunityProfileProvider> logger,
        TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _time = time;
    }

    private enum Outcome { Found, Missing, Failed }

    private sealed record Lookup(Outcome Outcome, SteamProfile? Profile);

    private sealed record CachedProfile(SteamProfile? Profile);

    public async Task<IReadOnlyDictionary<string, SteamProfile>> GetProfilesAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, SteamProfile>();
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

        if (missing.Count == 0 || IsPaused())
            return result;

        var lookups = missing.Take(MaxLookupsPerCall)
            .Select(id => (Id: id, Task: _inFlight.GetOrAdd(id, NewLookup).Value))
            .ToList();

        try
        {
            // The shared lookups aren't cancelled with this request: another roster may be waiting on them.
            await Task.WhenAll(lookups.Select(l => l.Task)).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The roster request was abandoned; return what's known. The lookups finish and fill the cache.
        }

        foreach (var (id, task) in lookups)
        {
            if (task.IsCompletedSuccessfully && task.Result.Profile != null)
                result[id] = task.Result.Profile;
        }
        return result;
    }

    private Lazy<Task<Lookup>> NewLookup(string steamId)
    {
        Lazy<Task<Lookup>>? lazy = null;
        lazy = new Lazy<Task<Lookup>>(() =>
        {
            var task = LookupAsync(steamId);
            // Leave the in-flight map once done, so the cache (or the next lookup) takes over.
            task.ContinueWith(
                _ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<Lookup>>>(steamId, lazy!)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        });
        return lazy;
    }

    private async Task<Lookup> LookupAsync(string steamId)
    {
        // Yields first, so the caller's GetOrAdd(...).Value returns before any network work starts.
        await Task.Yield();
        await _concurrency.WaitAsync();
        try
        {
            if (IsPaused())
                return new Lookup(Outcome.Failed, null);

            var lookup = await FetchAsync(steamId);
            if (lookup.Outcome != Outcome.Failed)
            {
                _cache.Set(steamId, new CachedProfile(lookup.Profile), new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = lookup.Outcome == Outcome.Found ? FoundLifetime : MissingLifetime
                });
            }
            return lookup;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task<Lookup> FetchAsync(string steamId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync($"https://steamcommunity.com/profiles/{steamId}/?xml=1");

            if ((int)response.StatusCode == 429)
            {
                _logger.LogWarning("Steam Community rate-limited profile lookups; pausing them.");
                Pause(PauseAfterRateLimit);
                return new Lookup(Outcome.Failed, null);
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Steam Community returned HTTP {StatusCode} for a profile lookup.", (int)response.StatusCode);
                Pause(PauseAfterFailure);
                return new Lookup(Outcome.Failed, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = XmlReader.Create(stream, SafeXml);
            var root = (await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None)).Root;
            return Parse(steamId, root);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
        {
            _logger.LogWarning(ex, "Steam Community profile lookup failed.");
            Pause(PauseAfterFailure);
            return new Lookup(Outcome.Failed, null);
        }
    }

    // <profile> with <steamID64>, <steamID> (the persona name) and <avatarMedium> for an existing account,
    // private or not; <response><error> when Steam has no such profile.
    private static Lookup Parse(string steamId, XElement? root)
    {
        if (root?.Name.LocalName == "response" && root.Element("error") != null)
            return new Lookup(Outcome.Missing, null);
        if (root?.Name.LocalName != "profile" || (string?)root.Element("steamID64") != steamId)
            return new Lookup(Outcome.Failed, null);

        var name = ((string?)root.Element("steamID"))?.Trim();
        if (string.IsNullOrEmpty(name))
            return new Lookup(Outcome.Missing, null);
        if (name.Length > MaxPersonaNameLength)
            name = name[..MaxPersonaNameLength];

        var avatar = (string?)root.Element("avatarMedium") ?? (string?)root.Element("avatarIcon");
        return new Lookup(Outcome.Found, new SteamProfile(name, SafeAvatarUrl(avatar?.Trim())));
    }

    private bool IsPaused() => _time.GetUtcNow().UtcTicks < Interlocked.Read(ref _pausedUntilTicks);

    private void Pause(TimeSpan duration) =>
        Interlocked.Exchange(ref _pausedUntilTicks, (_time.GetUtcNow() + duration).UtcTicks);

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

    public void Dispose()
    {
        _cache.Dispose();
        _concurrency.Dispose();
    }
}
