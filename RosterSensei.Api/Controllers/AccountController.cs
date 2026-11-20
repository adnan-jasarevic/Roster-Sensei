using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RosterSensei.Api.Controllers;

// The signed-in user's own account settings.
[Authorize]
[Route("api/[controller]")]
public class AccountController : ApiControllerBase
{
    private readonly IAccountService _accountService;

    public AccountController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet("steam")]
    public async Task<IActionResult> GetSteamAccount(CancellationToken cancellationToken)
        => ToActionResult(await _accountService.GetSteamAccountAsync(UserId, cancellationToken));

    // A Steam account is linked only by signing in through Steam, which proves the user owns it.
    [HttpPost("steam/sign-in")]
    public async Task<IActionResult> StartSteamSignIn([FromBody] StartSteamSignInDto dto)
        => ToActionResult(await _accountService.StartSteamSignInAsync(UserId, dto));

    // Not cancelled when the browser goes away: the one-time sign-in is already used up by then,
    // so finishing the link is better than leaving the user to start over.
    [HttpPost("steam/sign-in/complete")]
    public async Task<IActionResult> CompleteSteamSignIn([FromBody] CompleteSteamSignInDto dto)
        => ToActionResult(await _accountService.CompleteSteamSignInAsync(UserId, dto));

    [HttpDelete("steam")]
    public async Task<IActionResult> UnlinkSteamAccount()
        => ToMessageResult(await _accountService.UnlinkSteamAccountAsync(UserId));
}
