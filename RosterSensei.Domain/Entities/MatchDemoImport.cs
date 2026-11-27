namespace RosterSensei.Domain.Entities;

// The stats read from one map's demo. The demo file itself is read in the browser and not kept.
// A match's lineup totals are the sums over all of its imported maps.
public class MatchDemoImport
{
    public int Id { get; set; }

    public int MatchId { get; set; }
    public Match Match { get; set; } = null!;

    // Copied from the match, so a demo can be imported only once per team.
    public int TeamId { get; set; }

    // Which map of the match this demo is (MatchMap.Order), and its name when imported. Editing the
    // match so that this map is gone or renamed removes the import.
    public int MapOrder { get; set; }
    public string MapName { get; set; } = string.Empty;

    // SHA-256 of the demo file, as lowercase hex.
    public string Fingerprint { get; set; } = string.Empty;

    // The CS2 patch the demo was recorded on, and the parser version that read it.
    public int? PatchVersion { get; set; }
    public string ParserVersion { get; set; } = string.Empty;

    public int Rounds { get; set; }

    public string ImportedByUserId { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }

    public ICollection<DemoPlayerStats> Players { get; set; } = new List<DemoPlayerStats>();
}

// One of our players on one imported map.
public class DemoPlayerStats
{
    public int Id { get; set; }

    public int MatchDemoImportId { get; set; }
    public MatchDemoImport MatchDemoImport { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public string SteamId { get; set; } = string.Empty;

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int Damage { get; set; }
    public int HeadshotKills { get; set; }
}

// Demo import is paused for a parser version once it fails on a CS2 patch newer than any imported
// so far, which usually means a game update changed the demo format. A new parser version (a new
// release of the app) starts unpaused.
public class DemoParserStatus
{
    public string ParserVersion { get; set; } = string.Empty;
    public int PausedForPatch { get; set; }
    public DateTime PausedAt { get; set; }
}
