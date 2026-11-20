namespace RosterSensei.Application.Interfaces;

// The user's linked Steam account. SteamId is null when none is linked.
public sealed record SteamLink(string? SteamId, bool Verified);

public interface IUserRepository
{
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default);

    // Null when the user doesn't exist.
    Task<SteamLink?> GetSteamLinkAsync(string userId, CancellationToken cancellationToken = default);

    // Links a Steam account the user proved they own by signing in through Steam. An unverified (pasted)
    // link of the same account on another user is removed, since that user never proved it was theirs.
    // Conflict when another user has it verified; the unique index also catches two links racing.
    Task<WriteOutcome> LinkVerifiedSteamIdAsync(string userId, string steamId);

    // Rejected when the user doesn't exist.
    Task<WriteOutcome> ClearSteamIdAsync(string userId);
}
