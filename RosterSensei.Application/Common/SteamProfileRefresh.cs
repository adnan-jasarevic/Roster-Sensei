namespace RosterSensei.Application.Common;

// How often stored Steam names and avatars are looked up again.
public static class SteamProfileRefresh
{
    // Adnan's call: names and avatars don't change often, so once a week is enough.
    public static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    // When Steam didn't answer (down, rate-limiting, profile gone), try that account again a day later.
    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(1);

    // For accounts that must never be looked up, like the master tester tools' made-up players, whose
    // fake Steam IDs could belong to real strangers.
    public static readonly DateTime Never = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
}
