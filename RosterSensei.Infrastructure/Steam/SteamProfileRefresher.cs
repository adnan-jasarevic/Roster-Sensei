using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RosterSensei.Application.Common;
using RosterSensei.Application.Interfaces;
using RosterSensei.Infrastructure.Persistence;

namespace RosterSensei.Infrastructure.Steam;

// One pass of the background Steam upkeep: looks up the Steam names and avatars that are due (a week after
// the last lookup, or right away for a new link) and deletes Steam sign-ins nobody came back from.
// Scoped: each pass gets its own DbContext, so it holds a pooled connection only while it runs.
public sealed class SteamProfileRefresher
{
    // Profiles looked up per batch (the profile provider looks up at most 25 per call), and batches per pass.
    public const int BatchSize = 25;
    public const int MaxBatchesPerPass = 8;

    // A claimed account isn't picked again for this long, so a pass that dies half-way doesn't block it for good,
    // and two API instances running the job at once never look up the same account twice.
    public static readonly TimeSpan ClaimLease = TimeSpan.FromHours(1);

    private readonly AppDbContext _context;
    private readonly ISteamProfileProvider _profiles;
    private readonly IUserRepository _users;
    private readonly ISteamSignInStateStore _signIns;
    private readonly TimeProvider _time;
    private readonly ILogger<SteamProfileRefresher> _logger;

    public SteamProfileRefresher(
        AppDbContext context,
        ISteamProfileProvider profiles,
        IUserRepository users,
        ISteamSignInStateStore signIns,
        TimeProvider time,
        ILogger<SteamProfileRefresher> logger)
    {
        _context = context;
        _profiles = profiles;
        _users = users;
        _signIns = signIns;
        _time = time;
        _logger = logger;
    }

    // Returns how many profiles Steam answered for.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var expired = await _signIns.DeleteExpiredAsync(cancellationToken);
        if (expired > 0)
            _logger.LogDebug("Deleted {Count} expired Steam sign-ins.", expired);

        var refreshed = 0;
        for (var batch = 0; batch < MaxBatchesPerPass; batch++)
        {
            var (claimed, found) = await RefreshBatchAsync(cancellationToken);
            refreshed += found;
            // Nothing left that's due, or Steam answered for none of them (down or rate-limiting): wait for the next pass.
            if (claimed < BatchSize || found == 0)
                break;
        }
        if (refreshed > 0)
            _logger.LogInformation("Refreshed {Count} Steam names and avatars.", refreshed);
        return refreshed;
    }

    private async Task<(int Claimed, int Found)> RefreshBatchAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var due = await _context.Users
            .Where(u => u.SteamId != null && (u.SteamProfileRefreshDueAt == null || u.SteamProfileRefreshDueAt <= now))
            .OrderBy(u => u.SteamProfileRefreshDueAt)
            .Take(BatchSize)
            .Select(u => new { u.Id, SteamId = u.SteamId!, u.SteamProfileRefreshDueAt })
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
            return (0, 0);

        // Claim each account with a conditional update: if another instance (or a relink) changed it since it
        // was read, the update matches nothing and this pass leaves it alone.
        var claimed = new List<(string UserId, string SteamId)>();
        var leaseEnd = now + ClaimLease;
        foreach (var user in due)
        {
            var updated = await _context.Users
                .Where(u => u.Id == user.Id && u.SteamId == user.SteamId && u.SteamProfileRefreshDueAt == user.SteamProfileRefreshDueAt)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SteamProfileRefreshDueAt, leaseEnd), cancellationToken);
            if (updated == 1)
                claimed.Add((user.Id, user.SteamId));
        }
        if (claimed.Count == 0)
            return (due.Count, 0);

        var profiles = await _profiles.GetProfilesAsync(claimed.Select(c => c.SteamId).Distinct().ToList(), cancellationToken);

        // Saved even if the job is stopping, so what Steam answered isn't lost and nothing stays claimed.
        var found = 0;
        var nextRefresh = now + SteamProfileRefresh.Interval;
        var retry = now + SteamProfileRefresh.RetryAfter;
        foreach (var (userId, steamId) in claimed)
        {
            if (profiles.TryGetValue(steamId, out var profile))
            {
                await _users.SaveSteamProfileAsync(userId, steamId, profile, nextRefresh);
                found++;
            }
            else
            {
                // Steam didn't answer, or has no such profile: keep the old name and avatar, try again later.
                await _users.SaveSteamProfileAsync(userId, steamId, null, retry);
            }
        }
        return (due.Count, found);
    }
}
