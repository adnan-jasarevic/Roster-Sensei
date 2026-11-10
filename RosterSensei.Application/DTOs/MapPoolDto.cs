namespace RosterSensei.Application.DTOs;

// Response of GET /api/teams/{teamId}/map-pool: everything the Map Pool tab shows.
public class MapPoolDto
{
    // The maps that can be rated, in the pool's order.
    public List<string> Maps { get; set; } = new();

    // The team's members, one column each.
    public List<MapPoolPlayerDto> Players { get; set; } = new();

    // The caller: the one column they can edit.
    public string CurrentUserId { get; set; } = string.Empty;

    // Every rating given so far. A missing (player, map, side) means not rated yet.
    public List<MapRatingDto> Ratings { get; set; } = new();
}

public class MapPoolPlayerDto
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class MapRatingDto
{
    public string UserId { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    // "T" or "CT".
    public string Side { get; set; } = string.Empty;
    public decimal Rating { get; set; }
}

// Body of PUT /api/teams/{teamId}/map-pool/{side}/{mapName}: the caller's own rating for that cell.
public class SaveMapRatingDto
{
    // 1 to 10, with at most one decimal, e.g. 6.5.
    public decimal? Rating { get; set; }
}
