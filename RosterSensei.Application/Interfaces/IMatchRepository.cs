using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;

namespace RosterSensei.Application.Interfaces;

public enum LineupWriteOutcome
{
    Done,
    MatchNotFound,
    // The lineup has ratings or stats, but the match isn't finished.
    MatchNotFinished,
    // A player isn't a member of the team (and wasn't already in this lineup).
    NotTeamMembers,
    // A Coach was added who wasn't already in this lineup.
    CoachInLineup,
    // The database gave up on the write, e.g. as a deadlock victim. Retrying may work.
    Conflict
}

public interface IMatchRepository
{
    Task<Match> AddAsync(Match match);

    // The match with its maps and lineup, tracked for an edit. Null when the team has no such match.
    Task<Match?> GetForEditAsync(int teamId, int matchId);

    // Saves an edited match if its version is still expectedVersion, and bumps the version.
    // False when someone else changed or removed the match in the meantime.
    Task<bool> UpdateAsync(Match match, int expectedVersion);
    // With maps and lineups.
    Task<List<Match>> GetMatchesByTeamIdAsync(int teamId, CancellationToken cancellationToken = default);

    // Replaces a match's whole lineup in one transaction. Every player must be a member of the team,
    // or already in this lineup (someone who has left since keeps their place in its history).
    // With requireFinished, the match must be finished; checked under the same lock as the write.
    Task<LineupWriteOutcome> ReplaceLineupAsync(int teamId, int matchId, List<MatchParticipation> lineup, bool requireFinished = false);

    // Played maps of finished matches, grouped by map in the database. WinRatePercentage is left at 0.
    Task<List<MapStatsDto>> GetMapStatsAsync(int teamId, MatchKind? type, CancellationToken cancellationToken = default);
}
