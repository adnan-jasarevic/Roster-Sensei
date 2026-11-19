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

    // The player's scoreboard line for the whole match (all maps of a series together). Each is optional
    // and, like the rating, only for players who played in a finished match.
    public int? Kills { get; set; }
    public int? Deaths { get; set; }
    public int? Assists { get; set; }
    // Average damage per round, with at most one decimal.
    public decimal? Adr { get; set; }
    // Share of kills that were headshots, 0 to 100 with at most one decimal.
    public decimal? HeadshotPercentage { get; set; }

    // True when anything beyond who played and their role was recorded.
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
    // Kills, deaths and assists: a whole Bo5 stays well under this.
    public const int MaxCount = 999;
    public const decimal MaxAdr = 999.9m;
    public const decimal MaxHeadshotPercentage = 100m;
}
