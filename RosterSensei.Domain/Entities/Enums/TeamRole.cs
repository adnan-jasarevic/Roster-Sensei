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
    // Runs the team like the Captain and Officers but doesn't play: Coaches can't be put in a lineup,
    // so they have no player statistics. A team can have several Coaches.
    Coach = 4
}

public static class TeamRoleExtensions
{
    // The Captain, Officers and Coaches run the team day to day: invites, matches and the calendar.
    // They all have the same rights. The Owner can always do this too, whatever their role.
    public static bool CanManageTeam(this TeamRole role) =>
        role == TeamRole.Captain || role == TeamRole.Officer || role == TeamRole.Coach;

    // Everyone but Coaches can be put in a match lineup.
    public static bool CanPlay(this TeamRole role) => role != TeamRole.Coach;

    // Ownership can be handed to an Officer or a Coach.
    public static bool CanReceiveOwnership(this TeamRole role) =>
        role == TeamRole.Officer || role == TeamRole.Coach;
}

