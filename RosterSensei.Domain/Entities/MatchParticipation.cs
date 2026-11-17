namespace RosterSensei.Domain.Entities;

// One player in a match's lineup.
public class MatchParticipation
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    // Free text, e.g. "IGL" or "AWPer". Null when the role wasn't recorded.
    public string? Role { get; set; }

    // False for a substitute who was in the lineup but didn't play.
    public bool Played { get; set; }

    // How well the player played this match, from PlayerRating.Min to PlayerRating.Max with at most
    // two decimals. Optional, and only for players who played in a finished match.
    public decimal? Rating { get; set; }
}

public static class PlayerRating
{
    public const decimal Min = 0.01m;
    public const decimal Max = 99.99m;
}
