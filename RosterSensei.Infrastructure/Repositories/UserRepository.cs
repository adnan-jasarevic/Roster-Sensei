using RosterSensei.Application.Interfaces;
using RosterSensei.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace RosterSensei.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.Users.AnyAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<SteamLink?> GetSteamLinkAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new SteamLink(u.SteamId, u.SteamVerified, u.SteamPersonaName, u.SteamAvatarUrl))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<WriteOutcome> LinkVerifiedSteamIdAsync(string userId, string steamId)
    {
        // Single-column UPDATEs: they don't load the user, so they can't overwrite a concurrent password or
        // profile change, and they don't trip Identity's ConcurrencyStamp check. The transaction makes
        // "free the unverified claim, then link" one step.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Users
                .Where(u => u.SteamId == steamId && u.Id != userId && !u.SteamVerified)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.SteamId, (string?)null)
                    .SetProperty(u => u.SteamPersonaName, (string?)null)
                    .SetProperty(u => u.SteamAvatarUrl, (string?)null)
                    .SetProperty(u => u.SteamProfileRefreshDueAt, (DateTime?)null));

            // A different account than before loses the old name and avatar; the same one keeps them.
            // Either way the background refresh picks it up soon, in case the lookup after sign-in fails.
            var updated = await _context.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.SteamPersonaName, u => u.SteamId == steamId ? u.SteamPersonaName : null)
                    .SetProperty(u => u.SteamAvatarUrl, u => u.SteamId == steamId ? u.SteamAvatarUrl : null)
                    .SetProperty(u => u.SteamProfileRefreshDueAt, (DateTime?)null)
                    .SetProperty(u => u.SteamId, steamId)
                    .SetProperty(u => u.SteamVerified, true));

            if (updated != 1)
                return WriteOutcome.Rejected;

            await transaction.CommitAsync();
            return WriteOutcome.Done;
        }
        // The unique index on SteamId: another user has this account verified, or linked it at the same moment.
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            return WriteOutcome.Conflict;
        }
    }

    public async Task<WriteOutcome> ClearSteamIdAsync(string userId)
    {
        var updated = await _context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.SteamId, (string?)null)
                .SetProperty(u => u.SteamVerified, false)
                .SetProperty(u => u.SteamPersonaName, (string?)null)
                .SetProperty(u => u.SteamAvatarUrl, (string?)null)
                .SetProperty(u => u.SteamProfileRefreshDueAt, (DateTime?)null));

        return updated == 1 ? WriteOutcome.Done : WriteOutcome.Rejected;
    }

    public async Task SaveSteamProfileAsync(string userId, string steamId, SteamProfile? profile, DateTime nextRefreshAt)
    {
        var query = _context.Users.Where(u => u.Id == userId && u.SteamId == steamId);
        if (profile == null)
        {
            await query.ExecuteUpdateAsync(s => s.SetProperty(u => u.SteamProfileRefreshDueAt, nextRefreshAt));
            return;
        }

        await query.ExecuteUpdateAsync(s => s
            .SetProperty(u => u.SteamPersonaName, profile.PersonaName)
            .SetProperty(u => u.SteamAvatarUrl, profile.AvatarUrl)
            .SetProperty(u => u.SteamProfileRefreshDueAt, nextRefreshAt));
    }
}
