using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;

namespace RosterSensei.Application.Interfaces;

public interface IAccountService
{
    Task<Result<SteamAccountDto>> GetSteamAccountAsync(string userId, CancellationToken cancellationToken = default);

    // Step 1 of linking: returns the Steam page to send the browser to.
    Task<Result<SteamSignInStartedDto>> StartSteamSignInAsync(string userId, StartSteamSignInDto dto);

    // Step 2: checks Steam's reply and links the Steam account it proves the user owns.
    Task<Result<SteamAccountDto>> CompleteSteamSignInAsync(string userId, CompleteSteamSignInDto dto, CancellationToken cancellationToken = default);

    Task<Result<string>> UnlinkSteamAccountAsync(string userId);
}
