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
    private readonly ISteamSignInStateStore _states;
    private readonly SteamSignInSettings _settings;
    private readonly TimeProvider _time;

    public AccountService(
        IUserRepository userRepository,
        ISteamOpenIdVerifier openId,
        ISteamProfileProvider profiles,
        ISteamSignInStateStore states,
        SteamSignInSettings settings,
        TimeProvider time)
    {
        _userRepository = userRepository;
        _openId = openId;
        _profiles = profiles;
        _states = states;
        _settings = settings;
        _time = time;
    }

    public async Task<Result<SteamAccountDto>> GetSteamAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var link = await _userRepository.GetSteamLinkAsync(userId, cancellationToken);
        if (link == null)
            return Result<SteamAccountDto>.NotFound("User not found.");

        // The stored name and avatar: this page never waits on Steam.
        return Result<SteamAccountDto>.SuccessResult(ToDto(link));
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
        var issued = await _states.IssueAsync(userId, s => $"{baseUrl}?{StateParameter}={s}");
        if (issued == null)
            return Result<SteamSignInStartedDto>.Failure("Too many Steam sign-ins are in progress. Try again in a few minutes.");

        return Result<SteamSignInStartedDto>.SuccessResult(new SteamSignInStartedDto
        {
            RedirectUrl = _openId.BuildSignInUrl(issued.ReturnTo, origin)
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
        if (state == null || !await _states.TryConsumeAsync(state, userId, returnTo))
            return Result<SteamAccountDto>.Failure(Expired);

        var steamId = await _openId.VerifyAsync(parameters, cancellationToken);
        if (steamId == null)
            return Result<SteamAccountDto>.Failure(NotConfirmed);

        var outcome = await _userRepository.LinkVerifiedSteamIdAsync(userId, steamId);
        if (outcome == WriteOutcome.Conflict)
            return Result<SteamAccountDto>.Conflict(AlreadyLinked);
        if (outcome != WriteOutcome.Done)
            return Result<SteamAccountDto>.NotFound("User not found.");

        // A fresh link: the name and avatar are worth one lookup now, so the user sees them straight away.
        // The link itself is already saved; if Steam doesn't answer, the background refresh fills them in soon.
        var profiles = await _profiles.GetProfilesAsync([steamId], CancellationToken.None);
        if (profiles.TryGetValue(steamId, out var profile))
            await _userRepository.SaveSteamProfileAsync(userId, steamId, profile, _time.GetUtcNow().UtcDateTime + SteamProfileRefresh.Interval);

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

    private static SteamAccountDto ToDto(SteamLink link) => new()
    {
        SteamId = link.SteamId,
        ProfileUrl = link.SteamId == null ? null : SteamId.ProfileUrl(link.SteamId),
        Verified = link.SteamId != null && link.Verified,
        PersonaName = link.SteamId == null ? null : link.PersonaName,
        AvatarUrl = link.SteamId == null ? null : link.AvatarUrl
    };
}
