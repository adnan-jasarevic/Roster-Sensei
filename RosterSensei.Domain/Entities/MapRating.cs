using RosterSensei.Domain.Enums;

namespace RosterSensei.Domain.Entities;

// How strong one team member thinks the team is on one side of one map, from 1 to 10 (higher is better).
// It belongs to the membership, so it goes when the member leaves or is removed, or the team is deleted.
public class MapRating
{
    public int Id { get; set; }

    public int TeamId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public TeamMember Member { get; set; } = null!;

    // One of MapPool.Maps, spelled as listed there.
    public string MapName { get; set; } = string.Empty;
    public MapSide Side { get; set; }

    // 1.0 to 10.0, with at most one decimal.
    public decimal Rating { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
