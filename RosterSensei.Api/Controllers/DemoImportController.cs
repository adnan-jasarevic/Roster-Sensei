using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RosterSensei.Api.Controllers;

// The browser reads demo files itself and only sends their scoreboard here.
[Authorize]
[Route("api/[controller]")]
public class DemoImportController : ApiControllerBase
{
    private readonly IDemoImportService _demoImportService;

    public DemoImportController(IDemoImportService demoImportService)
    {
        _demoImportService = demoImportService;
    }

    // GET: /api/DemoImport/status?parserVersion=demoparser2%200.42.3
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] string parserVersion)
        => ToActionResult(await _demoImportService.GetStatusAsync(parserVersion ?? string.Empty));

    // POST: /api/DemoImport/{teamId}/{matchId}/preview
    [HttpPost("{teamId}/{matchId}/preview")]
    public async Task<IActionResult> Preview(int teamId, int matchId, [FromBody] PreviewDemoImportDto dto)
        => ToActionResult(await _demoImportService.PreviewAsync(UserId, teamId, matchId, dto));

    // POST: /api/DemoImport/{teamId}/{matchId}
    [HttpPost("{teamId}/{matchId}")]
    public async Task<IActionResult> Apply(int teamId, int matchId, [FromBody] ApplyDemoImportDto dto)
        => ToActionResult(await _demoImportService.ApplyAsync(UserId, teamId, matchId, dto));

    // POST: /api/DemoImport/{teamId}/failures
    [HttpPost("{teamId}/failures")]
    public async Task<IActionResult> ReportReadFailure(int teamId, [FromBody] DemoReadFailureDto dto)
        => ToActionResult(await _demoImportService.ReportReadFailureAsync(UserId, teamId, dto));
}
