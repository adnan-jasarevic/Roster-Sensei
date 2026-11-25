namespace RosterSensei.Application.Interfaces;

// The user's linked Steam account. SteamId is null when none is linked. PersonaName and AvatarUrl are the
// stored public Steam name and avatar, null until Steam has answered once.
public sealed record SteamLink(string? SteamId, bool Verified, string? PersonaName, string? AvatarUrl);

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

    // Stores the Steam name and avatar (when profile isn't null) and when to look them up next. Only writes
    // while the user still has steamId linked, so a lookup that finishes after an unlink or relink can't put
    // the wrong account's name back.
    Task SaveSteamProfileAsync(string userId, string steamId, SteamProfile? profile, DateTime nextRefreshAt);
}
