using RosterSensei.Domain.Entities;
using RosterSensei.Domain.Enums;

namespace RosterSensei.Application.Interfaces;

public interface IMapRatingRepository
{
    Task<List<MapRating>> GetForTeamAsync(int teamId, CancellationToken cancellationToken = default);

    // Sets the member's rating for one map and side, adding it if they hadn't rated it yet.
    // Rejected: the user isn't (or is no longer) a member of the team.
    // Conflict: a simultaneous save of the same rating got in between and this one couldn't be applied.
    Task<WriteOutcome> SaveAsync(MapRating rating);

    // Returns false when the member hadn't rated that map and side.
    Task<bool> DeleteAsync(int teamId, string userId, string mapName, MapSide side);
}
