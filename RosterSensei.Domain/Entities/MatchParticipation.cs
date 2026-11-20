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

    // Totals for the whole match. Optional; same rules as the rating.
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }
    // Average damage per round, one decimal.
    public decimal? Adr { get; set; }
    // Headshot percentage, 0 to 100, one decimal.
    public decimal? HeadshotPercentage { get; set; }

    // True when a rating or any stat is recorded.
    public bool HasStats =>
        Rating.HasValue || Kills.HasValue || Deaths.HasValue || Assists.HasValue || Adr.HasValue || HeadshotPercentage.HasValue;

    public void ClearStats()
    {
        Rating = null;
        Kills = null;
        Deaths = null;
        Assists = null;
        Adr = null;
        HeadshotPercentage = null;
    }
}

public static class PlayerRating
{
    public const decimal Min = 0.01m;
    public const decimal Max = 99.99m;
}

public static class PlayerMatchStats
{
    // For kills, deaths and assists.
    public const int MaxCount = 999;
    public const decimal MaxAdr = 999.9m;
    public const decimal MaxHeadshotPercentage = 100m;
}
