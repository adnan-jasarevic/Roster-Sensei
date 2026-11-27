namespace RosterSensei.Application.DTOs;

// What the browser read from one map's demo. The numbers are the game's own end-of-match scoreboard.
public class DemoSummaryDto
{
    // Version of the parser that read the demo, e.g. "demoparser2 0.42.3".
    public string ParserVersion { get; set; } = string.Empty;
    // The CS2 patch the demo was recorded on, from the demo header. Null when the header has none.
    public int? PatchVersion { get; set; }
    // SHA-256 of the demo file, as lowercase hex.
    public string Fingerprint { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public int Rounds { get; set; }
    // Exactly two: the two teams, in any order.
    public List<DemoSideDto> Sides { get; set; } = new();
}

public class DemoSideDto
{
    // Rounds won: in total, and in the first and second regulation half. Anything past the halves is overtime.
    public int Score { get; set; }
    public int FirstHalf { get; set; }
    public int SecondHalf { get; set; }
    public List<DemoPlayerDto> Players { get; set; } = new();
    public List<DemoCoachDto> Coaches { get; set; } = new();
}

public class DemoPlayerDto
{
    public string SteamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int Damage { get; set; }
    public int HeadshotKills { get; set; }
}

public class DemoCoachDto
{
    public string SteamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

// Body of POST /api/DemoImport/{teamId}/{matchId}/preview.
public class PreviewDemoImportDto
{
    public DemoSummaryDto Demo { get; set; } = new();
}

// What an import would do, shown before saving.
public class DemoImportPreviewDto
{
    // 0 or 1: the side with the most team members on it. Null when nobody from the team is in the demo.
    public int? OurSide { get; set; }
    // The map of the match this demo would fill (MatchMap.Order). A number past the last map adds one.
    public int MapOrder { get; set; }
    // True when this map already has an imported demo that would be replaced.
    public bool ReplacesImport { get; set; }
    public List<DemoSidePreviewDto> Sides { get; set; } = new();
    // Coaches the match would have after the import: the ones already saved plus our side's coaches.
    public List<MatchCoachEntryDto> Coaches { get; set; } = new();
}

public class DemoSidePreviewDto
{
    public int Score { get; set; }
    public int FirstHalf { get; set; }
    public int SecondHalf { get; set; }
    public List<DemoPlayerPreviewDto> Players { get; set; } = new();
    public List<DemoCoachPreviewDto> Coaches { get; set; } = new();
}

public class DemoPlayerPreviewDto
{
    public string SteamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // The team member with this Steam account, or null.
    public string? UserId { get; set; }
    public string? Username { get; set; }
    // False for a team member who is a Coach: they can't be put in a lineup.
    public bool CanPlay { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public decimal Adr { get; set; }
    public decimal HeadshotPercentage { get; set; }
}

public class DemoCoachPreviewDto
{
    public string SteamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Username { get; set; }
}

// Body of POST /api/DemoImport/{teamId}/{matchId}.
public class ApplyDemoImportDto
{
    public DemoSummaryDto Demo { get; set; } = new();
    // Which side of the demo is our team: 0 or 1.
    public int OurSide { get; set; }
    // The map of the match to fill: an existing map's order, or one past the last to add a map.
    public int MapOrder { get; set; }
    // Who each of our side's players is. Null links them automatically by Steam account. When sent,
    // it lists every player of our side; a null UserId leaves that player out.
    public List<DemoPlayerLinkDto>? Players { get; set; }
    // The match's coaches after the import. Null keeps the saved ones and adds our side's coaches.
    public List<MatchCoachDto>? Coaches { get; set; }
    // The match version the preview was made from.
    public int Version { get; set; }
}

public class DemoPlayerLinkDto
{
    public string SteamId { get; set; } = string.Empty;
    public string? UserId { get; set; }
}

// Body of POST /api/DemoImport/{teamId}/failures: the browser couldn't read a demo.
public class DemoReadFailureDto
{
    public string ParserVersion { get; set; } = string.Empty;
    public int? PatchVersion { get; set; }
}

public class DemoImportStatusDto
{
    public bool Paused { get; set; }
    public string? Message { get; set; }
}
