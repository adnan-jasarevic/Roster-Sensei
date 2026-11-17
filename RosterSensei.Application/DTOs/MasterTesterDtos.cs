namespace RosterSensei.Application.DTOs;

// Master tester tools: one-click test data for local development. See MasterTesterController.

public class MasterTesterStatusDto
{
    // The parts of a team that can be filled, in the order the panel lists them.
    public List<string> Sections { get; set; } = new();
}

public class SeedTeamDto
{
    // Optional; a made-up name is picked when empty.
    public string? Name { get; set; }

    // The tester's IANA time zone (e.g. "Europe/Sarajevo"), so calendar events land at sensible local times.
    public string? TimeZone { get; set; }
}

public class SeedSectionDto
{
    // "add" keeps what's there and adds more; "replace" clears the section first.
    public string Mode { get; set; } = "add";

    public string? TimeZone { get; set; }
}

public class SeedResultDto
{
    public int? TeamId { get; set; }
    public string Message { get; set; } = string.Empty;
}
