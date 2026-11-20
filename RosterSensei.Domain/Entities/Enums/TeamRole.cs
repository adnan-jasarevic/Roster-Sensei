namespace RosterSensei.Domain.Enums;

// The Owner isn't a role: ownership is kept on the Team, so the Owner also has one of these roles
// (Captain by default, for whoever creates the team).
public enum TeamRole
{
    Captain = 1,
    Player = 2,
    // Second-in-command. A team can have several Officers but only one Captain,
    // and ownership can only be transferred to an Officer or a Coach.
    Officer = 3,
    // Manages the team like an Officer but doesn't play. A team can have several Coaches.
    Coach = 4
}

public static class TeamRoleExtensions
{
    // Invites, matches and the calendar. The Owner can always do this too, whatever their role.
    public static bool CanManageTeam(this TeamRole role) =>
        role == TeamRole.Captain || role == TeamRole.Officer || role == TeamRole.Coach;

    // Coaches can't be put in a lineup.
    public static bool CanPlay(this TeamRole role) => role != TeamRole.Coach;

    public static bool CanReceiveOwnership(this TeamRole role) =>
        role == TeamRole.Officer || role == TeamRole.Coach;
}

