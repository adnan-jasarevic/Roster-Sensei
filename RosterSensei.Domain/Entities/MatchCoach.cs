namespace RosterSensei.Domain.Entities;

// A coach who was with the team for a match. A match can have several, or none.
public class MatchCoach
{
    public int Id { get; set; }

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    // The team member who coached, or null for a coach from outside the team.
    public string? UserId { get; set; }

    // Shown for a coach from outside the team. For a team member it keeps the name they had in the demo, if any.
    public string? Name { get; set; }

    // SteamID64, when known (always for a coach read from a demo).
    public string? SteamId { get; set; }
}

public static class MatchCoachLimits
{
    public const int MaxCoaches = 5;
    public const int MaxNameLength = 64;
}
