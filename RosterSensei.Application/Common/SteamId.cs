using System.Text.RegularExpressions;

namespace RosterSensei.Application.Common;

// Reads a Steam account from what a user is likely to paste and turns it into a SteamID64,
// the 17-digit ID that Steam's profile URLs and Web API use.
public static partial class SteamId
{
    // SteamID64 of an individual account on the public universe is this base plus the 32-bit account number.
    private const ulong IndividualBase = 76561197960265728UL;
    private const ulong MaxAccountNumber = uint.MaxValue;

    [GeneratedRegex(@"^\d{17}$")]
    private static partial Regex SteamId64Pattern();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?steamcommunity\.com/profiles/(\d{17})/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileUrlPattern();

    // STEAM_X:Y:Z, where the account number is Z * 2 + Y.
    [GeneratedRegex(@"^STEAM_[0-5]:([01]):(\d{1,10})$", RegexOptions.IgnoreCase)]
    private static partial Regex SteamId2Pattern();

    // [U:1:N], where N is the account number.
    [GeneratedRegex(@"^\[?U:1:(\d{1,10})\]?$", RegexOptions.IgnoreCase)]
    private static partial Regex SteamId3Pattern();

    public static bool TryParse(string? input, out string steamId64)
    {
        steamId64 = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var value = input.Trim();

        if (SteamId64Pattern().IsMatch(value))
            return TryFromSteamId64(value, out steamId64);

        var profile = ProfileUrlPattern().Match(value);
        if (profile.Success)
            return TryFromSteamId64(profile.Groups[1].Value, out steamId64);

        var id2 = SteamId2Pattern().Match(value);
        if (id2.Success && ulong.TryParse(id2.Groups[2].Value, out var half))
            return TryFromAccountNumber(half * 2 + ulong.Parse(id2.Groups[1].Value), out steamId64);

        var id3 = SteamId3Pattern().Match(value);
        if (id3.Success && ulong.TryParse(id3.Groups[1].Value, out var accountNumber))
            return TryFromAccountNumber(accountNumber, out steamId64);

        return false;
    }

    public static string ProfileUrl(string steamId64) => $"https://steamcommunity.com/profiles/{steamId64}";

    private static bool TryFromSteamId64(string digits, out string steamId64)
    {
        steamId64 = string.Empty;
        if (!ulong.TryParse(digits, out var value) || value <= IndividualBase)
            return false;

        return TryFromAccountNumber(value - IndividualBase, out steamId64);
    }

    private static bool TryFromAccountNumber(ulong accountNumber, out string steamId64)
    {
        steamId64 = string.Empty;
        // Account number 0 is not a real account.
        if (accountNumber == 0 || accountNumber > MaxAccountNumber)
            return false;

        steamId64 = (IndividualBase + accountNumber).ToString();
        return true;
    }
}
