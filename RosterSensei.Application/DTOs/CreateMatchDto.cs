using RosterSensei.Domain.Entities;

namespace RosterSensei.Application.DTOs;

public class CreateMatchDto
{
    public string OpponentName { get; set; } = string.Empty;
    public string? Tournament { get; set; }
    public DateTime ScheduledDate { get; set; }
    public DateTime? PlayedDate { get; set; }
    public MatchStatus Status { get; set; }
    public MatchKind Type { get; set; }
    // Ignored when any map has a score: the series score is then worked out from the maps.
    public int OurScore { get; set; }
    public int OpponentScore { get; set; }
    public int? BestOf { get; set; }
    public List<MatchMapDto> Maps { get; set; } = new List<MatchMapDto>();
    public string? Notes { get; set; }
    // Optional: who played, with optional ratings (finished matches only). Same rules as the lineup endpoint.
    // On an edit, null keeps the current lineup (minus its ratings, if the match is no longer finished).
    public List<LineupPlayerDto>? Lineup { get; set; }
}

// Body of PUT /api/Matches/{teamId}/{matchId}: the whole match, as when adding it.
public class UpdateMatchDto : CreateMatchDto
{
    // The version the edit started from. A newer one in the database means someone else saved first.
    public int Version { get; set; }
}
