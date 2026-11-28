using RosterSensei.Domain.Entities;

namespace RosterSensei.Application.Services;

// Turns a user's local availability (a repeating week plus overrides) into real moments in UTC.
public static class AvailabilityExpander
{
    private static readonly long SlotTicks = TimeSpan.FromMinutes(AvailabilitySlots.SlotMinutes).Ticks;

    // The UTC start of every free half hour in [from, to). Both bounds are UTC.
    //
    // Each local half hour maps to one UTC half hour. On the night clocks go forward, the skipped
    // local hour doesn't exist, so it is free for nobody. On the night they go back, the repeated
    // local hour is counted once, at its first occurrence. In a zone whose offset isn't a whole half
    // hour (Nepal, +5:45) a slot is rounded down to the UTC half hour it starts in.
    public static IEnumerable<DateTime> FreeSlots(UserAvailability availability, TimeZoneInfo timeZone, DateTime from, DateTime to)
    {
        var overrides = availability.Overrides.ToDictionary(o => o.Date, o => o.Slots);

        // One local day either side, so slots near the range edges in any offset are looked at.
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(from, timeZone)).AddDays(-1);
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(to, timeZone)).AddDays(1);

        var seen = new HashSet<DateTime>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var slots = SlotsFor(availability, overrides, day);
            var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

            for (var slot = 0; slot < AvailabilitySlots.PerDay; slot++)
            {
                if (slots[slot] != AvailabilitySlots.Free)
                    continue;

                var local = midnight.AddMinutes(slot * AvailabilitySlots.SlotMinutes);
                if (timeZone.IsInvalidTime(local))
                    continue;

                var utc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
                utc = new DateTime(utc.Ticks - utc.Ticks % SlotTicks, DateTimeKind.Utc);

                if (utc >= from && utc < to && seen.Add(utc))
                    yield return utc;
            }
        }
    }

    // The day's own override if it has one, otherwise that weekday of the repeating week.
    public static string SlotsFor(UserAvailability availability, IReadOnlyDictionary<DateOnly, string> overrides, DateOnly day) =>
        overrides.TryGetValue(day, out var slots)
            ? slots
            : availability.WeeklySlots.Substring(AvailabilitySlots.DayIndex(day.DayOfWeek) * AvailabilitySlots.PerDay, AvailabilitySlots.PerDay);

    // The saved zone, or UTC if this machine's time zone data no longer has it.
    public static TimeZoneInfo ZoneOf(UserAvailability availability) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(availability.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;
}
