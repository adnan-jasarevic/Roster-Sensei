namespace RosterSensei.Application.Interfaces;

public interface IIdentityService
{
    Task<(bool Exists, string UserId)> CheckUserExistsAndGetIdAsync(string email);
    Task<string> GetUsernameByIdAsync(string userId);

    // Usernames for many users in one query, keyed by user ID. Unknown IDs are left out.
    Task<Dictionary<string, string>> GetUsernamesByIdsAsync(IEnumerable<string> userIds);

    // User IDs of the accounts linked to these SteamID64s, keyed by Steam ID. Unlinked ones are left out.
    Task<Dictionary<string, string>> GetUserIdsBySteamIdsAsync(IEnumerable<string> steamIds);
}
