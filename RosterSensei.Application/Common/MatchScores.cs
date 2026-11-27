using RosterSensei.Domain.Entities;

namespace RosterSensei.Application.Common;

public static class MatchScores
{
    // Works out the series score from the played maps: rounds for a Bo1, maps won otherwise.
    // Returns null when no map has a score, so the score entered for the match is used instead.
    public static (int Our, int Opponent)? FromMaps(IEnumerable<MatchMap> maps, int? bestOf)
    {
        var played = maps.Where(m => m.OurScore.HasValue && m.OpponentScore.HasValue).ToList();
        if (played.Count == 0)
            return null;

        if ((bestOf ?? 1) == 1 && played.Count == 1)
            return (played[0].OurScore!.Value, played[0].OpponentScore!.Value);

        return (
            played.Count(m => m.OurScore > m.OpponentScore),
            played.Count(m => m.OpponentScore > m.OurScore));
    }
}
