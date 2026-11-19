using System.Text.Json.Serialization;

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
    // The scoreboard line for the whole match. Same rules as the rating; each is optional.
    // Kills, deaths and assists: 0 to 999.
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }
    // 0 to 999.9, at most one decimal.
    public decimal? Adr { get; set; }
    // 0 to 100, at most one decimal.
    public decimal? HeadshotPercentage { get; set; }

    [JsonIgnore]
    public bool HasStats =>
        Rating.HasValue || Kills.HasValue || Deaths.HasValue || Assists.HasValue || Adr.HasValue || HeadshotPercentage.HasValue;
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
    // Each null when not recorded.
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }
    public decimal? Adr { get; set; }
    public decimal? HeadshotPercentage { get; set; }
}
