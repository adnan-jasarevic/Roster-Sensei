using RosterSensei.Application.Common;
using RosterSensei.Application.DTOs;

namespace RosterSensei.Application.Interfaces;

// Fills teams with made-up but realistic data, for the master tester account only.
public interface IMasterTesterService
{
    // True when the tools are switched on and this user is one of the configured master testers.
    Task<bool> IsMasterTesterAsync(string userId);

    // Creates a team owned by the tester and fills every section.
    Task<Result<SeedResultDto>> CreateFullTeamAsync(string userId, SeedTeamDto dto);

    // Fills one section of a team the tester is in. Section is one of MasterTesterSections.All.
    Task<Result<SeedResultDto>> FillSectionAsync(string userId, int teamId, string section, SeedSectionDto dto);

    // Sends the tester pending invites from made-up teams, for testing the notification bell.
    Task<Result<SeedResultDto>> SendInvitesAsync(string userId, SeedSectionDto dto);
}

public static class MasterTesterSections
{
    public const string Roster = "roster";
    public const string Matches = "matches";
    public const string Calendar = "calendar";
    public const string OffDays = "offdays";
    public const string MapPool = "mappool";

    public static readonly IReadOnlyList<string> All = [Roster, Matches, Calendar, OffDays, MapPool];
}
