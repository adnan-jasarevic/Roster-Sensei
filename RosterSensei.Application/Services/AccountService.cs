using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;
using RosterSensei.Application.Interfaces;

namespace RosterSensei.Application.Services;

public class AccountService : IAccountService
{
    public const string StateParameter = "rs_state";

    private const string AlreadyLinked = "This Steam account is already linked to another Roster Sensei account.";
    private const string Expired = "This Steam sign-in expired or was already used. Please sign in through Steam again.";
    private const string NotConfirmed = "Steam couldn't confirm this sign-in. Please try again.";

    private readonly IUserRepository _userRepository;
    private readonly ISteamOpenIdVerifier _openId;
    private readonly ISteamProfileProvider _profiles;
    private readonly SteamSignInStates _states;
    private readonly SteamSignInSettings _settings;

    public AccountService(
        IUserRepository userRepository,
        ISteamOpenIdVerifier openId,
        ISteamProfileProvider profiles,
        SteamSignInStates states,
        SteamSignInSettings settings)
    {
        _userRepository = userRepository;
        _openId = openId;
        _profiles = profiles;
        _states = states;
        _settings = settings;
    }

    public async Task<Result<SteamAccountDto>> GetSteamAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var link = await _userRepository.GetSteamLinkAsync(userId, cancellationToken);
        if (link == null)
            return Result<SteamAccountDto>.NotFound("User not found.");

        var dto = ToDto(link.SteamId, link.Verified);
        if (link.SteamId != null)
        {
            var profiles = await _profiles.GetProfilesAsync([link.SteamId], cancellationToken);
            if (profiles.TryGetValue(link.SteamId, out var profile))
            {
                dto.PersonaName = profile.PersonaName;
                dto.AvatarUrl = profile.AvatarUrl;
            }
        }
        return Result<SteamAccountDto>.SuccessResult(dto);
    }

    public async Task<Result<SteamSignInStartedDto>> StartSteamSignInAsync(string userId, StartSteamSignInDto dto)
    {
        if (!Uri.TryCreate(dto.ReturnUrl, UriKind.Absolute, out var returnUrl)
            || (returnUrl.Scheme != Uri.UriSchemeHttps && returnUrl.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(returnUrl.Fragment)
            || !string.IsNullOrEmpty(returnUrl.UserInfo))
            return Result<SteamSignInStartedDto>.Failure("The return URL is not valid.");

        // Only our own frontend may receive the sign-in reply.
        var origin = returnUrl.GetLeftPart(UriPartial.Authority);
        if (!_settings.AllowedOrigins.Contains(origin))
            return Result<SteamSignInStartedDto>.Failure("Steam sign-in can't return to this site.");

        if (!await _userRepository.UserExistsAsync(userId))
            return Result<SteamSignInStartedDto>.NotFound("User not found.");

        var baseUrl = returnUrl.GetLeftPart(UriPartial.Path);
        var state = _states.Issue(userId, s => $"{baseUrl}?{StateParameter}={s}", out var returnTo);
        if (state == null)
            return Result<SteamSignInStartedDto>.Failure("Too many Steam sign-ins are in progress. Try again in a few minutes.");

        return Result<SteamSignInStartedDto>.SuccessResult(new SteamSignInStartedDto
        {
            RedirectUrl = _openId.BuildSignInUrl(returnTo, origin)
        });
    }

    public async Task<Result<SteamAccountDto>> CompleteSteamSignInAsync(string userId, CompleteSteamSignInDto dto, CancellationToken cancellationToken = default)
    {
        var parameters = dto.Parameters;
        var mode = parameters.GetValueOrDefault("openid.mode");
        if (mode == "cancel")
            return Result<SteamAccountDto>.Failure("Steam sign-in was cancelled.");
        if (mode != "id_res")
            return Result<SteamAccountDto>.Failure(NotConfirmed);

        // The return URL is part of what Steam signs, so the state read from it can't be swapped.
        var returnTo = parameters.GetValueOrDefault("openid.return_to");
        if (returnTo == null || !Uri.TryCreate(returnTo, UriKind.Absolute, out var returnUri))
            return Result<SteamAccountDto>.Failure(NotConfirmed);

        var state = ReadState(returnUri.Query);
        // Consumed before talking to Steam, so two requests with the same reply can't both get through.
        if (state == null || !_states.TryConsume(state, userId, returnTo))
            return Result<SteamAccountDto>.Failure(Expired);

        var steamId = await _openId.VerifyAsync(parameters, cancellationToken);
        if (steamId == null)
            return Result<SteamAccountDto>.Failure(NotConfirmed);

        var outcome = await _userRepository.LinkVerifiedSteamIdAsync(userId, steamId);
        if (outcome == WriteOutcome.Conflict)
            return Result<SteamAccountDto>.Conflict(AlreadyLinked);
        if (outcome != WriteOutcome.Done)
            return Result<SteamAccountDto>.NotFound("User not found.");

        // A fresh link: the name and avatar are worth one lookup, but the link itself is already saved.
        return await GetSteamAccountAsync(userId, CancellationToken.None);
    }

    public async Task<Result<string>> UnlinkSteamAccountAsync(string userId)
    {
        // Unlinking is idempotent: clearing an empty link still succeeds.
        return await _userRepository.ClearSteamIdAsync(userId) == WriteOutcome.Done
            ? Result<string>.SuccessResult("Steam account unlinked.")
            : Result<string>.NotFound("User not found.");
    }

    private static string? ReadState(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == StateParameter)
                return Uri.UnescapeDataString(pair[1]);
        }
        return null;
    }

    private static SteamAccountDto ToDto(string? steamId, bool verified) => new()
    {
        SteamId = steamId,
        ProfileUrl = steamId == null ? null : SteamId.ProfileUrl(steamId),
        Verified = steamId != null && verified
    };
}
