namespace RosterSensei.Application.Interfaces;

public sealed record SteamProfile(string PersonaName, string? AvatarUrl);

// Public Steam names and avatars from the Steam Web API.
public interface ISteamProfileProvider
{
    // Profiles for the IDs Steam knows. Returns an empty map, never throws, when no API key is set,
    // Steam is down, or the request is cancelled, so a roster always loads.
    Task<IReadOnlyDictionary<string, SteamProfile>> GetProfilesAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken = default);
}

// Used when nothing better is registered, e.g. in tests: no names or avatars.
public sealed class NoSteamProfiles : ISteamProfileProvider
{
    public static readonly NoSteamProfiles Instance = new();

    public Task<IReadOnlyDictionary<string, SteamProfile>> GetProfilesAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, SteamProfile>>(new Dictionary<string, SteamProfile>());
}
