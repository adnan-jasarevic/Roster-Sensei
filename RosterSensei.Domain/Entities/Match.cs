using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RosterSensei.Domain.Entities;

public class Match
{
    public int Id { get; set; }

    [Required]
    public string OpponentName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Tournament { get; set; }

    public DateTime ScheduledDate { get; set; }
    public DateTime? PlayedDate { get; set; }
    public MatchStatus Status { get; set; }  // Scheduled, Finished or Cancelled
    public MatchKind Type { get; set; }

    // Series score: rounds for a Bo1, maps won for a longer series
    public int OurScore { get; set; }
    public int OpponentScore { get; set; }

    public int? BestOf { get; set; }  // e.g., 3

    // Worked out from the series score, so every endpoint reports results the same way.
    public MatchOutcome Outcome =>
        OurScore > OpponentScore ? MatchOutcome.Win
        : OurScore < OpponentScore ? MatchOutcome.Loss
        : MatchOutcome.Draw;

    public string FinalScore => $"{OurScore} - {OpponentScore}";

    public ICollection<MatchMap> Maps { get; set; } = new List<MatchMap>();

    public string? Notes { get; set; }

    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public ICollection<MatchParticipation> Participations { get; set; } = new List<MatchParticipation>();

    // Goes up by one on every edit. An edit sends the version it started from, so a save over
    // someone else's newer changes is refused instead of silently overwriting them.
    public int Version { get; set; }
}

// Stored as these numbers. 1 was "Live", removed: the AddMatchVersion migration turned any Live match into Scheduled.
public enum MatchStatus
{
    Scheduled = 0,
    Finished = 2,
    Cancelled = 3
}

public enum MatchKind
{
    Official,
    Scrim
}
