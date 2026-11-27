namespace RosterSensei.Application.DTOs;

// A coach of a match. Either a team member (UserId) or someone from outside the team (Name).
public class MatchCoachDto
{
    public string? UserId { get; set; }
    public string? Name { get; set; }
    // SteamID64, optional.
    public string? SteamId { get; set; }
}

public class MatchCoachEntryDto
{
    // Null for a coach from outside the team.
    public string? UserId { get; set; }
    // The member's username, or the name entered for a coach from outside the team.
    public string DisplayName { get; set; } = string.Empty;
    public string? SteamId { get; set; }
}
