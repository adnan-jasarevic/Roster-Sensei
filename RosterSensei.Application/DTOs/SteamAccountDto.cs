namespace RosterSensei.Application.DTOs;

public class SteamAccountDto
{
    // Null when no Steam account is linked.
    public string? SteamId { get; set; }
    public string? ProfileUrl { get; set; }

    // True when the link was made by signing in through Steam. Links pasted before Steam sign-in existed are false.
    public bool Verified { get; set; }

    // Public Steam name and avatar, when the Steam Web API key is set and Steam answered. Otherwise null.
    public string? PersonaName { get; set; }
    public string? AvatarUrl { get; set; }
}
