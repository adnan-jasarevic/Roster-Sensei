namespace RosterSensei.Infrastructure.Persistence;

// A Steam sign-in that was started and hasn't come back yet. Id is a SHA-256 hash of the one-time token in
// the return URL, so the database never holds a token that could be replayed.
public class SteamSignInState
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ReturnTo { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
