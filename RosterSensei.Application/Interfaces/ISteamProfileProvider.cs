namespace RosterSensei.Application.Interfaces;

public sealed record SteamProfile(string PersonaName, string? AvatarUrl);

// Public Steam names and avatars from Steam Community profiles.
public interface ISteamProfileProvider
{
    // Profiles for the IDs Steam knows. Leaves out what it can't get (Steam down or rate-limiting, request
    // cancelled) and never throws, so a roster always loads.
    Task<IReadOnlyDictionary<string, SteamProfile>> GetProfilesAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken = default);
}

// Used when nothing better is registered, e.g. in tests: no names or avatars.
public sealed class NoSteamProfiles : ISteamProfileProvider
{
    public static readonly NoSteamProfiles Instance = new();

    public Task<IReadOnlyDictionary<string, SteamProfile>> GetProfilesAsync(IReadOnlyCollection<string> steamIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, SteamProfile>>(new Dictionary<string, SteamProfile>());
}
