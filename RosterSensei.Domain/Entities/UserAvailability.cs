namespace RosterSensei.Domain.Entities;

// When a user can play: a week that repeats, plus dates that differ from it. It belongs to the user,
// not to a team, so someone in several teams fills it in once and every team sees the same times.
public class UserAvailability
{
    public string UserId { get; set; } = string.Empty;

    // The IANA time zone (e.g. "Europe/Sarajevo") the times are in. A pattern like "Tuesdays 19:00 to
    // 23:00" stays 19:00 local across daylight saving changes, so it can't be stored in UTC.
    public string TimeZone { get; set; } = string.Empty;

    // Seven days of half-hour slots, Monday first: one character per slot, '1' free and '0' not.
    // Character day * 48 + slot is the slot starting at slot * 30 minutes past local midnight.
    public string WeeklySlots { get; set; } = AvailabilitySlots.EmptyWeek;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<AvailabilityOverride> Overrides { get; set; } = new();
}

// One date whose availability differs from the repeating week. It replaces that whole day.
public class AvailabilityOverride
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public UserAvailability Availability { get; set; } = null!;

    // The date in the availability's time zone.
    public DateOnly Date { get; set; }

    // One day of half-hour slots, in the same form as a day of the repeating week.
    public string Slots { get; set; } = AvailabilitySlots.EmptyDay;
}

public static class AvailabilitySlots
{
    public const int SlotMinutes = 30;
    public const int PerDay = 24 * 60 / SlotMinutes;
    public const int PerWeek = PerDay * 7;

    public const char Free = '1';
    public const char Busy = '0';

    public static readonly string EmptyDay = new(Busy, PerDay);
    public static readonly string EmptyWeek = new(Busy, PerWeek);

    // How many players a lineup needs, so how many must be free before a time shows on the calendar.
    public const int PlayersNeeded = 5;

    public const int MaxTimeZoneLength = 64;

    public static bool IsDay(string? slots) => slots is { Length: PerDay } && slots.All(c => c is Free or Busy);

    // Monday = 0 ... Sunday = 6.
    public static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;
}
