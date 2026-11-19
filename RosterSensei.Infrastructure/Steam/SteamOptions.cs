namespace RosterSensei.Infrastructure.Steam;

// Bound from the "Steam" configuration section.
public sealed class SteamOptions
{
    public const string Section = "Steam";

    // Steam Web API key (https://steamcommunity.com/dev/apikey). Only needed for names and avatars on the
    // roster; sign-in works without it. Keep it in user-secrets or an environment variable (Steam__WebApiKey),
    // never in appsettings.json.
    public string? WebApiKey { get; set; }
}
