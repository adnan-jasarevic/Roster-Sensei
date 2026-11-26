using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RosterSensei.Application.Interfaces;
using RosterSensei.Infrastructure.Persistence;

namespace RosterSensei.Infrastructure.Repositories;

public sealed class SteamSignInStateStore : ISteamSignInStateStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // Expired sign-ins are deleted on every Issue and by the background job; this cap bounds the table even
    // if someone starts sign-ins in a loop faster than they expire.
    public const int MaxPending = 10_000;
    public const int MaxPendingPerUser = 5;

    private readonly AppDbContext _context;
    private readonly TimeProvider _time;

    public SteamSignInStateStore(AppDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    public async Task<IssuedSteamSignIn?> IssueAsync(string userId, Func<string, string> buildReturnTo)
    {
        var now = Now();
        await _context.SteamSignInStates.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync();

        if (await _context.SteamSignInStates.CountAsync() >= MaxPending)
            return null;

        // A user only needs one sign-in at a time; drop their oldest so retries never get stuck.
        var stale = await _context.SteamSignInStates
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.ExpiresAt)
            .Skip(MaxPendingPerUser - 1)
            .Select(s => s.Id)
            .ToListAsync();
        if (stale.Count > 0)
            await _context.SteamSignInStates.Where(s => stale.Contains(s.Id)).ExecuteDeleteAsync();

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var returnTo = buildReturnTo(state);
        _context.SteamSignInStates.Add(new SteamSignInState
        {
            Id = Hash(state),
            UserId = userId,
            ReturnTo = returnTo,
            ExpiresAt = now + Lifetime
        });
        await _context.SaveChangesAsync();
        return new IssuedSteamSignIn(state, returnTo);
    }

    public async Task<bool> TryConsumeAsync(string state, string userId, string returnTo)
    {
        var id = Hash(state);
        var pending = await _context.SteamSignInStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (pending == null)
            return false;

        // The delete decides who gets it: when two requests race, only one deletes the row.
        if (await _context.SteamSignInStates.Where(s => s.Id == id).ExecuteDeleteAsync() != 1)
            return false;

        return pending.UserId == userId
            && pending.ReturnTo == returnTo
            && pending.ExpiresAt > Now();
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = Now();
        return await _context.SteamSignInStates.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    // Tokens are 64 hex characters, so anything else can't match; hashing still handles it safely.
    private static string Hash(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
}
