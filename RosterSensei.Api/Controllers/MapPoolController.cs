using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RosterSensei.Api.Controllers;

// Every member rates each side of each map for themselves. There's no user ID in the routes: the
// ratings changed are always the caller's own, so nobody can edit someone else's column.
[Authorize]
[Route("api/teams/{teamId}/map-pool")]
public class MapPoolController : ApiControllerBase
{
    private readonly IMapPoolService _mapPoolService;

    public MapPoolController(IMapPoolService mapPoolService)
    {
        _mapPoolService = mapPoolService;
    }

    // GET: /api/teams/{teamId}/map-pool
    [HttpGet]
    public async Task<IActionResult> GetMapPool(int teamId, CancellationToken cancellationToken)
        => ToActionResult(await _mapPoolService.GetAsync(UserId, teamId, cancellationToken));

    // PUT: /api/teams/{teamId}/map-pool/T/Mirage  { "rating": 6.5 }
    [HttpPut("{side}/{mapName}")]
    public async Task<IActionResult> SaveRating(int teamId, string side, string mapName, [FromBody] SaveMapRatingDto dto)
        => ToActionResult(await _mapPoolService.SaveRatingAsync(UserId, teamId, side, mapName, dto));

    // DELETE: /api/teams/{teamId}/map-pool/CT/Nuke
    [HttpDelete("{side}/{mapName}")]
    public async Task<IActionResult> ClearRating(int teamId, string side, string mapName)
        => ToMessageResult(await _mapPoolService.ClearRatingAsync(UserId, teamId, side, mapName));
}
