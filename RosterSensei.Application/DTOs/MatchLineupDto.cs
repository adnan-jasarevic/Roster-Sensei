namespace RosterSensei.Application.DTOs;

// Body of PUT /api/Matches/{teamId}/{matchId}/lineup. Replaces the whole lineup; an empty list clears it.
public class SetMatchLineupDto
{
    public List<LineupPlayerDto> Players { get; set; } = new();
}

public class LineupPlayerDto
{
    public string UserId { get; set; } = string.Empty;
    // Free text, up to 30 characters. Empty or null means not recorded.
    public string? Role { get; set; }
    public bool Played { get; set; } = true;
    // 0.01 to 99.99 with at most two decimals, or null when not rated. Only for a player who played,
    // in a finished match.
    public decimal? Rating { get; set; }
}

public class MatchLineupEntryDto
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    // Null when not recorded.
    public string? Role { get; set; }
    public bool Played { get; set; }
    // Null when not rated.
    public decimal? Rating { get; set; }
}
