using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RosterSensei.Application.Common;

// One-time tokens that tie a Steam sign-in to the user who started it. Without them, someone could send
// a victim a link that finishes the attacker's own Steam sign-in on the victim's Roster Sensei account.
// Kept in memory: a sign-in started on one API instance must finish on the same one, which holds while
// the API runs as a single instance.
public sealed class SteamSignInStates
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Abandoned sign-ins are pruned on every Issue; this cap bounds memory even if someone starts sign-ins
    // in a loop faster than they expire.
    public const int MaxPending = 10_000;
    public const int MaxPendingPerUser = 5;

    private sealed record Pending(string UserId, string ReturnTo, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public SteamSignInStates(TimeProvider time) => _time = time;

    // Null when too many sign-ins are already waiting.
    public string? Issue(string userId, Func<string, string> buildReturnTo, out string returnTo)
    {
        returnTo = string.Empty;
        var now = _time.GetUtcNow();
        Prune(now);

        if (_pending.Count >= MaxPending)
            return null;

        // A user only needs one sign-in at a time; drop their oldest so retries never get stuck.
        var mine = _pending.Where(p => p.Value.UserId == userId).OrderBy(p => p.Value.ExpiresAt).ToList();
        foreach (var old in mine.Take(Math.Max(0, mine.Count - (MaxPendingPerUser - 1))))
            _pending.TryRemove(old.Key, out _);

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        returnTo = buildReturnTo(state);
        _pending[state] = new Pending(userId, returnTo, now + Lifetime);
        return state;
    }

    // Removes the state whatever happens, so a reply can be tried once only. True when it was issued to
    // this user for exactly this return URL and hasn't expired.
    public bool TryConsume(string state, string userId, string returnTo)
    {
        if (!_pending.TryRemove(state, out var pending))
            return false;

        return pending.UserId == userId
            && pending.ReturnTo == returnTo
            && pending.ExpiresAt > _time.GetUtcNow();
    }

    public int PendingCount => _pending.Count;

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, value) in _pending)
        {
            if (value.ExpiresAt <= now)
                _pending.TryRemove(new KeyValuePair<string, Pending>(key, value));
        }
    }
}

// Origins Steam may send users back to: the frontend origins from Cors:AllowedOrigins.
public sealed class SteamSignInSettings
{
    public IReadOnlyCollection<string> AllowedOrigins { get; }

    public SteamSignInSettings(IEnumerable<string> allowedOrigins)
    {
        AllowedOrigins = allowedOrigins
            .Select(o => o.Trim().TrimEnd('/'))
            .Where(o => o.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
