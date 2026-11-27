namespace RosterSensei.Application.Interfaces;

public interface IDemoImportRepository
{
    // Where this team already imported the demo with this fingerprint, or null.
    Task<(int MatchId, int MapOrder, string OpponentName)?> FindImportAsync(int teamId, string fingerprint);

    // The newest CS2 patch any demo was imported from, or null when none has been.
    Task<int?> NewestImportedPatchAsync();

    // The patch this parser version was paused for, or null when it isn't paused.
    Task<int?> PausedForPatchAsync(string parserVersion);

    // Pauses this parser version. Safe to call more than once, also at the same time.
    Task PauseAsync(string parserVersion, int patchVersion, DateTime now);
}
