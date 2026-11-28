using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RosterSensei.Api.Controllers;

// Availability belongs to the user, so their own lives under /availability, and a team reads its
// members' through /teams/{teamId}/availability.
[Authorize]
[Route("api")]
public class AvailabilityController : ApiControllerBase
{
    private readonly IAvailabilityService _availabilityService;

    public AvailabilityController(IAvailabilityService availabilityService)
    {
        _availabilityService = availabilityService;
    }

    // GET: /api/availability
    [HttpGet("availability")]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
        => ToActionResult(await _availabilityService.GetMineAsync(UserId, cancellationToken));

    // PUT: /api/availability/weekly
    [HttpPut("availability/weekly")]
    public async Task<IActionResult> SaveWeekly([FromBody] SaveWeeklyAvailabilityDto dto)
        => ToActionResult(await _availabilityService.SaveWeeklyAsync(UserId, dto));

    // PUT: /api/availability/days/2026-11-20
    [HttpPut("availability/days/{date}")]
    public async Task<IActionResult> SaveDay(DateOnly date, [FromBody] SaveAvailabilityOverrideDto dto)
        => ToActionResult(await _availabilityService.SaveOverrideAsync(UserId, date, dto));

    // DELETE: /api/availability/days/2026-11-20
    [HttpDelete("availability/days/{date}")]
    public async Task<IActionResult> ResetDay(DateOnly date)
        => ToMessageResult(await _availabilityService.DeleteOverrideAsync(UserId, date));

    // GET: /api/teams/{teamId}/availability?from=...&to=...
    [HttpGet("teams/{teamId}/availability")]
    public async Task<IActionResult> GetTeam(int teamId, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken cancellationToken)
        => ToActionResult(await _availabilityService.GetTeamAsync(UserId, teamId, from.ToUniversalTime(), to.ToUniversalTime(), cancellationToken));
}
