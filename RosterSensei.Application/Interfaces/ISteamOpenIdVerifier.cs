namespace RosterSensei.Application.Interfaces;

// Steam's OpenID 2.0 sign-in. It only proves which Steam account a person signed in to:
// Roster Sensei never sees their Steam password and gets no access to the account.
public interface ISteamOpenIdVerifier
{
    // The steamcommunity.com page that asks the user to sign in and then returns to returnTo.
    string BuildSignInUrl(string returnTo, string realm);

    // Checks a reply with Steam itself and returns the SteamID64 it vouches for, or null when the reply
    // is not genuine, was already used, or Steam couldn't be reached.
    Task<string?> VerifyAsync(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
}
