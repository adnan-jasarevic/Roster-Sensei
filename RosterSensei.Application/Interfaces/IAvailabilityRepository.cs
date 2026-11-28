using RosterSensei.Domain.Entities;

namespace RosterSensei.Application.Interfaces;

public interface IAvailabilityRepository
{
    // The user's availability with its overrides from overridesFrom onwards, or null if they never saved any.
    Task<UserAvailability?> GetAsync(string userId, DateOnly overridesFrom, CancellationToken cancellationToken = default);

    // The availability of each of these users who has saved some, with the overrides in [from, to].
    Task<List<UserAvailability>> GetForUsersAsync(IReadOnlyCollection<string> userIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    // Creates the user's availability or replaces its repeating week and time zone.
    Task SaveWeeklyAsync(string userId, string timeZone, string weeklySlots);

    // Sets one date's slots, creating the user's availability (with an empty week in timeZone) if it
    // doesn't exist yet. Overrides from before pruneBefore are removed while at it.
    Task SaveOverrideAsync(string userId, string timeZone, DateOnly date, string slots, DateOnly pruneBefore);

    // Returns false when the date had no override.
    Task<bool> DeleteOverrideAsync(string userId, DateOnly date);
}
