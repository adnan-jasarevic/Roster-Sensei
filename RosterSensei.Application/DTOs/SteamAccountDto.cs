namespace RosterSensei.Application.DTOs;

public class SteamAccountDto
{
    // Null when no Steam account is linked.
    public string? SteamId { get; set; }
    public string? ProfileUrl { get; set; }

    // True when the link was made by signing in through Steam. Links pasted before Steam sign-in existed are false.
    public bool Verified { get; set; }

    // Stored public Steam name and avatar, refreshed weekly. Null until Steam has answered once.
    public string? PersonaName { get; set; }
    public string? AvatarUrl { get; set; }
}
