namespace RosterSensei.Application.Common;

// Origins Steam may send users back to: the frontend origins from Cors:AllowedOrigins.
public sealed class SteamSignInSettings
{
    public IReadOnlyCollection<string> AllowedOrigins { get; }

    public SteamSignInSettings(IEnumerable<string> allowedOrigins)
    {
        AllowedOrigins = allowedOrigins
            .Select(o => o.Trim().TrimEnd('/'))
            .Where(o => o.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
