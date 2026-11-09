namespace RosterSensei.Application.DTOs;

public class TeamEventDto
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    // Empty when the event has no title of its own; show the event type's name instead.
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int EventTypeId { get; set; }
    public int Version { get; set; }
}

// Used for both creating and editing an event.
public class SaveTeamEventDto
{
    // Optional. Empty means "show the event type's name".
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    // One of the team's event types.
    public int EventTypeId { get; set; }

    // Required when editing: the Version of the event the user started from. Ignored when creating.
    public int? Version { get; set; }
}
