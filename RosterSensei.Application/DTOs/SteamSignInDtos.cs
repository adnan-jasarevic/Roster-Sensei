namespace RosterSensei.Application.DTOs;

public class StartSteamSignInDto
{
    // The page Steam sends the user back to. Its origin must be one of Cors:AllowedOrigins.
    public string ReturnUrl { get; set; } = string.Empty;
}

public class SteamSignInStartedDto
{
    // The steamcommunity.com sign-in page to send the browser to.
    public string RedirectUrl { get; set; } = string.Empty;
}

public class CompleteSteamSignInDto
{
    // Every openid.* query parameter Steam added to the return URL, unchanged.
    public Dictionary<string, string> Parameters { get; set; } = new();
}
