using RosterSensei.Application.DTOs;
using RosterSensei.Domain.Entities;

namespace RosterSensei.Application.Common;

public static class MatchCoachList
{
    public const string NotTeamMemberMessage = "A coach from the team must be a member of it. Enter a name instead for a coach from outside the team.";

    // Builds the coaches of a match from a request. Null when a listed member isn't in the team and
    // wasn't already a coach of this match (someone who has left since keeps their place in its history).
    public static List<MatchCoach>? Build(IEnumerable<MatchCoachDto> coaches, Team team, IEnumerable<MatchCoach>? existing = null)
    {
        var previous = (existing ?? Enumerable.Empty<MatchCoach>())
            .Where(c => c.UserId != null).Select(c => c.UserId!).ToHashSet();

        var result = new List<MatchCoach>();
        foreach (var coach in coaches)
        {
            var userId = string.IsNullOrWhiteSpace(coach.UserId) ? null : coach.UserId;
            if (userId != null && !previous.Contains(userId) && !team.Members.Any(m => m.UserId == userId))
                return null;

            result.Add(new MatchCoach
            {
                UserId = userId,
                Name = string.IsNullOrWhiteSpace(coach.Name) ? null : coach.Name.Trim(),
                SteamId = string.IsNullOrWhiteSpace(coach.SteamId) ? null : coach.SteamId
            });
        }
        return result;
    }

    public static List<MatchCoachEntryDto> ToDto(IEnumerable<MatchCoach> coaches, IReadOnlyDictionary<string, string> usernames) =>
        coaches
            .OrderBy(c => c.Id)
            .Select(c => new MatchCoachEntryDto
            {
                UserId = c.UserId,
                DisplayName = (c.UserId != null ? usernames.GetValueOrDefault(c.UserId) : null) ?? c.Name ?? "Unknown",
                SteamId = c.SteamId
            })
            .ToList();
}
