namespace RosterSensei.Application.DTOs;

// The signed-in user's own availability. Each day is 48 characters, one per half hour from local
// midnight: '1' free, '0' not.
public class MyAvailabilityDto
{
    // Null until the user has saved anything.
    public string? TimeZone { get; set; }

    // Seven days, Monday first.
    public List<string> Weekly { get; set; } = new();

    // Dates that differ from the repeating week, from a week ago onwards.
    public List<AvailabilityOverrideDto> Overrides { get; set; } = new();
}

public class AvailabilityOverrideDto
{
    public DateOnly Date { get; set; }
    public string Slots { get; set; } = string.Empty;
}

public class SaveWeeklyAvailabilityDto
{
    // The IANA time zone of the browser saving it.
    public string TimeZone { get; set; } = string.Empty;
    public List<string> Weekly { get; set; } = new();
}

public class SaveAvailabilityOverrideDto
{
    // Used only when this is the user's first save; otherwise the date is read in their saved time zone.
    public string TimeZone { get; set; } = string.Empty;
    public string Slots { get; set; } = string.Empty;
}

// Who on a team is free when, over a range of time.
public class TeamAvailabilityDto
{
    public int SlotMinutes { get; set; }

    // How many players must be free for a time to count as a lineup.
    public int PlayersNeeded { get; set; }

    public List<TeamAvailabilityMemberDto> Members { get; set; } = new();

    // Only half hours with at least one person free, in time order.
    public List<TeamAvailabilitySlotDto> Slots { get; set; } = new();
}

public class TeamAvailabilityMemberDto
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    // Coaches can fill in their availability, but they don't play, so they never count towards a lineup.
    public bool CountsTowardsLineup { get; set; }

    public bool HasFilledIn { get; set; }
}

public class TeamAvailabilitySlotDto
{
    // UTC start of the half hour.
    public DateTime Start { get; set; }
    public List<string> UserIds { get; set; } = new();
}
