using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RosterSensei.Api.Controllers;

// One-click test data for the master tester accounts listed under MasterTester:Emails.
// For anyone else, and always in Production, every endpoint answers 404 as if it didn't exist.
[Authorize]
[Route("api/master-tester")]
[ApiExplorerSettings(IgnoreApi = true)]
public class MasterTesterController : ApiControllerBase, IAsyncActionFilter
{
    private readonly IMasterTesterService _service;
    private readonly IWebHostEnvironment _environment;

    public MasterTesterController(IMasterTesterService service, IWebHostEnvironment environment)
    {
        _service = service;
        _environment = environment;
    }

    // Runs before every action here: the check can't be forgotten on a new endpoint.
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_environment.IsProduction() || !await _service.IsMasterTesterAsync(UserId))
        {
            context.Result = NotFound();
            return;
        }
        await next();
    }

    // GET: /api/master-tester  (200 means the tools are available to this user)
    [HttpGet]
    public IActionResult GetStatus()
        => Ok(new MasterTesterStatusDto { Sections = MasterTesterSections.All.ToList() });

    // POST: /api/master-tester/teams  { "name": null, "timeZone": "Europe/Sarajevo" }
    [HttpPost("teams")]
    public async Task<IActionResult> CreateFullTeam([FromBody] SeedTeamDto dto)
        => ToActionResult(await _service.CreateFullTeamAsync(UserId, dto));

    // POST: /api/master-tester/teams/5/matches  { "mode": "add" | "replace", "timeZone": "..." }
    [HttpPost("teams/{teamId}/{section}")]
    public async Task<IActionResult> FillSection(int teamId, string section, [FromBody] SeedSectionDto dto)
        => ToActionResult(await _service.FillSectionAsync(UserId, teamId, section, dto));

    // POST: /api/master-tester/invites  { "mode": "add" | "replace" }
    [HttpPost("invites")]
    public async Task<IActionResult> SendInvites([FromBody] SeedSectionDto dto)
        => ToActionResult(await _service.SendInvitesAsync(UserId, dto));
}
