namespace RosterSensei.Domain.Entities;

// The maps a team can rate on the Map Pool tab: the competitive pool, as offered when recording a match.
// Keep it in step with MAP_POOL in the client's constants/maps.ts.
public static class MapPool
{
    public const decimal MinRating = 1m;
    public const decimal MaxRating = 10m;

    public static readonly IReadOnlyList<string> Maps =
    [
        "Ancient",
        "Anubis",
        "Dust2",
        "Inferno",
        "Mirage",
        "Nuke",
        "Overpass",
        "Train",
        "Vertigo"
    ];

    // The map's name as listed, ignoring case, or null when it isn't in the pool.
    public static string? Find(string? mapName) =>
        Maps.FirstOrDefault(m => string.Equals(m, mapName?.Trim(), StringComparison.OrdinalIgnoreCase));
}
