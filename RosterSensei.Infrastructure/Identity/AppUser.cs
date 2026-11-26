using Microsoft.AspNetCore.Identity;
using RosterSensei.Domain.Entities;

namespace RosterSensei.Infrastructure.Identity;

public class AppUser : IdentityUser
{
    // SteamID64 of the linked Steam account, or null. Unique across users.
    public string? SteamId { get; set; }

    // True when the user proved they own SteamId by signing in through Steam. Links pasted before
    // Steam sign-in existed stay false until the user signs in through Steam.
    public bool SteamVerified { get; set; }

    // Public Steam name and avatar of SteamId, stored so rosters never wait on Steam. Filled right after a
    // Steam sign-in and kept fresh by the weekly background refresh; null until Steam has answered once.
    public string? SteamPersonaName { get; set; }
    public string? SteamAvatarUrl { get; set; }

    // When the background refresh should next look this user's Steam profile up. Null means as soon as possible.
    public DateTime? SteamProfileRefreshDueAt { get; set; }

    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
    public ICollection<MatchParticipation> MatchParticipations { get; set; } = new List<MatchParticipation>();
}
