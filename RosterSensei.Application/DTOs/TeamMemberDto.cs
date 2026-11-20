namespace RosterSensei.Application.DTOs;

public class TeamMemberDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public DateTime JoinedAt { get; set; }

    // SteamID64 and profile link of the member's linked Steam account, or null.
    public string? SteamId { get; set; }
    public string? SteamProfileUrl { get; set; }

    // True when the member proved the Steam account is theirs by signing in through Steam.
    public bool SteamVerified { get; set; }

    // Public Steam name and avatar, when Steam answered (it can be rate-limited or down). Otherwise null.
    public string? SteamPersonaName { get; set; }
    public string? SteamAvatarUrl { get; set; }
}
