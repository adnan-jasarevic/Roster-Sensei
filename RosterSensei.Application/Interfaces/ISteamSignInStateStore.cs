namespace RosterSensei.Application.Interfaces;

// A one-time token tying a Steam sign-in to the user who started it, and the return URL it was issued for.
public sealed record IssuedSteamSignIn(string State, string ReturnTo);

// One-time tokens that tie a Steam sign-in to the user who started it. Without them, someone could send a
// victim a link that finishes the attacker's own Steam sign-in on the victim's Roster Sensei account.
// Kept in the database, so a sign-in survives an API restart and can finish on any API instance.
public interface ISteamSignInStateStore
{
    // Null when too many sign-ins are already waiting. buildReturnTo turns the new token into the return URL.
    Task<IssuedSteamSignIn?> IssueAsync(string userId, Func<string, string> buildReturnTo);

    // Removes the state whatever happens, so a reply can be tried once only. True when it was issued to this
    // user for exactly this return URL and hasn't expired. Of two requests racing with the same state, only
    // one can get true.
    Task<bool> TryConsumeAsync(string state, string userId, string returnTo);

    // Deletes sign-ins nobody came back from. Returns how many.
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
