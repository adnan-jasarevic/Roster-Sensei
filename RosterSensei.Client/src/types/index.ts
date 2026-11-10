// types/index.ts

// Response of POST /Auth/login.
export interface LoginResponse {
    username: string;
    token: string;
}

/**
 * A member's role in a team, as the API names it. Matches TeamRole in RosterSensei.Domain.
 * The Owner isn't a role: ownership is the separate `isOwner` flag, and the Owner has a role too.
 */
export type TeamRole = 'Captain' | 'Officer' | 'Player';

/** Every role the Owner can give a member, in the order the role picker lists them. */
export const TEAM_ROLES: readonly TeamRole[] = ['Player', 'Officer', 'Captain'];

export interface Team {
    id: number;
    name: string;
    createdAt: string;
    currentUserRole: TeamRole;
    isOwner: boolean; // The Owner also has one of the roles above.
}

export interface MatchMap {
    mapName: string;
    ourScore?: number | null;
    opponentScore?: number | null;
    ourFirstHalf?: number | null;
    opponentFirstHalf?: number | null;
    ourSecondHalf?: number | null;
    opponentSecondHalf?: number | null;
}

/** Every endpoint sends match results in this exact form. */
export type MatchOutcome = 'Win' | 'Loss' | 'Draw';

/**
 * Where a match is in its life, as responses name it. Matches MatchStatus in RosterSensei.Domain.
 * Requests send the number instead: see the `MatchStatus` constant below.
 *
 * A match is created in one of these and stays there: the API has no endpoint that moves a match
 * to another status yet.
 */
export type MatchStatusName = 'Scheduled' | 'Live' | 'Finished' | 'Cancelled';

/** Official or scrim, as responses name it. Requests send the number: see `MatchKind` below. */
export type MatchKindName = 'Official' | 'Scrim';

export interface Match {
    id: number;
    opponentName: string;
    tournament?: string;
    scheduledDate: string;
    playedDate?: string;
    status: MatchStatusName;
    type: MatchKindName;
    /** "0 - 0" until the match is finished: only show it when `hasResult` is true. */
    finalScore: string;
    bestOf?: number;
    maps: MatchMap[];
    /** "Draw" until the match is finished: only show it when `hasResult` is true. */
    matchOutcome: MatchOutcome;
    notes?: string;
    /** Who was in the lineup, players who played first. Empty when it wasn't recorded. */
    lineup: LineupEntry[];
}

/** One player in a match lineup, as the API returns it. */
export interface LineupEntry {
    userId: string;
    username: string;
    /** Free text such as "AWP" or "IGL". Null when not recorded. */
    role: string | null;
    /** False for a substitute who was named but didn't play. */
    played: boolean;
}

/** Body of PUT /Matches/{teamId}/{matchId}/lineup. Replaces the whole lineup; an empty list clears it. */
export interface SetLineupRequest {
    players: { userId: string; role?: string; played: boolean }[];
}

/** Limits enforced by SetMatchLineupDtoValidator. */
export const LINEUP_MAX_PLAYERS = 10;
export const LINEUP_MAX_ROLE_LENGTH = 30;

/** The signed-in user's linked Steam account. Both fields are null when none is linked. */
export interface SteamAccount {
    steamId: string | null;
    profileUrl: string | null;
}

export interface MatchResponse {
    id: number;
    opponentName: string;
    scheduledDate: string;
    playedDate?: string;
    status: MatchStatusName;
    finalScore?: string;
    matchOutcome?: MatchOutcome;
}

export interface TeamDashboardData {
    totalMatches: number;
    winRatePercentage: number;
    totalMembers: number;
    upcomingMatchesCount: number;
    recentMatches: MatchResponse[];
    upcomingMatches: MatchResponse[];
}

export interface TeamMember {
    id: string;
    email: string;
    username: string;
    role: TeamRole;
    isOwner: boolean;
    joinedAt: string;
    /** SteamID64 and profile link of the member's linked Steam account, or null. */
    steamId?: string | null;
    steamProfileUrl?: string | null;
}

export interface AddTeamMemberRequest {
    userEmail: string;
}

export interface TeamInvite {
    inviteId: number;
    teamId: number;
    teamName: string;
    senderUsername: string;
    createdAt: string;
}


// Numeric values match the MatchStatus enum in RosterSensei.Domain.
export const MatchStatus = {
    Scheduled: 0,
    Live: 1,
    Finished: 2,
    Cancelled: 3,
} as const;

export type MatchStatusValue = typeof MatchStatus[keyof typeof MatchStatus];

// Numeric values match the MatchKind enum in RosterSensei.Domain.
export const MatchKind = {
    Official: 0,
    Scrim: 1,
} as const;

export type MatchKindValue = typeof MatchKind[keyof typeof MatchKind];

export interface CreateMatchRequest {
    opponentName: string;
    tournament?: string;
    scheduledDate: string;
    playedDate?: string;
    status: MatchStatusValue;
    type: MatchKindValue;
    ourScore: number;
    opponentScore: number;
    bestOf?: number;
    maps: MatchMap[];
    notes?: string;
}

/** The premade types, by the name the API uses. Matches EventTypePreset in RosterSensei.Domain. */
export type EventTypePreset =
    | 'Pracc' | 'OfficialMatch' | 'Scrim' | 'VodReview' | 'RecordedMatch'
    | 'Theory' | 'Break' | 'Warmup' | 'Meet' | 'Other';

/** One of the team's own event types. */
export interface TeamEventType {
    id: number;
    name: string;
    /** "#RRGGBB" */
    color: string;
    /** The premade type it was made from (kept when renamed), or null for a custom type. */
    preset: EventTypePreset | null;
    /** True for "Other", which can't be changed or removed. */
    isLocked: boolean;
}

export interface EventTypePresetOption {
    preset: EventTypePreset;
    name: string;
    color: string;
}

export interface TeamEventTypes {
    types: TeamEventType[];
    /** Premade types the team doesn't have right now. */
    availablePresets: EventTypePresetOption[];
}

export interface SaveEventTypeRequest {
    name: string;
    color: string;
}

/** A day with no play. Events on it show as cancelled; no new ones can be added. */
export interface TeamOffDay {
    /** "yyyy-MM-dd" */
    date: string;
    startTime: string;
    endTime: string;
}

export interface TeamEvent {
    id: number;
    teamId: number;
    title: string;
    description?: string | null;
    startTime: string;
    endTime: string;
    eventTypeId: number;
    /** Goes up on every edit; sent back when saving so a newer edit isn't overwritten. */
    version: number;
}

export interface SaveTeamEventRequest {
    title: string;
    description?: string;
    startTime: string;
    endTime: string;
    eventTypeId: number;
    /** Required when editing: the version of the event the edit started from. */
    version?: number;
}

export interface MapStats {
    mapName: string;
    played: number;
    wins: number;
    losses: number;
    draws: number;
    roundsWon: number;
    roundsLost: number;
    winRatePercentage: number;
}

export interface FormPoint {
    matchId: number;
    date: string;
    opponentName: string;
    matchOutcome: MatchOutcome;
    winRatePercentage: number;
}

export interface TeamStats {
    matchesPlayed: number;
    mapsPlayed: number;
    maps: MapStats[];
    form: FormPoint[];
}

// The Owner, the Captain and Officers can invite players and manage matches and the calendar.
export const canManageTeam = (team?: Team) =>
    !!team && (team.isOwner || team.currentUserRole === 'Captain' || team.currentUserRole === 'Officer');

// How the user's place in a team is shown, for example "Owner, Captain".
export const roleLabel = (team: Team) => team.isOwner ? `Owner, ${team.currentUserRole}` : team.currentUserRole;

// Ownership can only be handed to an Officer. The API enforces the same rule.
export const canReceiveOwnership = (member: TeamMember) => !member.isOwner && member.role === 'Officer';

// The match was played, so its score and outcome mean something.
export const hasResult = (match: Pick<Match, 'status'>) => match.status === 'Finished';

// Scheduled for a time that hasn't come yet. The dashboard's "Upcoming" uses the same rule on the
// server (TeamService.GetTeamDashboardAsync).
export const isUpcoming = (match: Pick<Match, 'status' | 'scheduledDate'>, now: Date) =>
    match.status === 'Scheduled' && new Date(match.scheduledDate) > now;

// Still Scheduled although its time has passed: nobody has recorded what happened yet.
export const needsResult = (match: Pick<Match, 'status' | 'scheduledDate'>, now: Date) =>
    match.status === 'Scheduled' && new Date(match.scheduledDate) <= now;

/** A side of a map. Matches MapSide in RosterSensei.Domain. */
export type MapSide = 'T' | 'CT';

/** Response of GET /teams/{teamId}/map-pool. */
export interface MapPool {
    /** The maps that can be rated, in the pool's order. */
    maps: string[];
    /** One column per team member. */
    players: { userId: string; username: string }[];
    /** The signed-in user: the only column they can edit. */
    currentUserId: string;
    /** A missing (player, map, side) means not rated yet. */
    ratings: MapRating[];
}

export interface MapRating {
    userId: string;
    mapName: string;
    side: MapSide;
    /** 1 to 10, at most one decimal. */
    rating: number;
}

/** Body of PUT /teams/{teamId}/map-pool/{side}/{mapName}. DELETE on the same path clears the rating. */
export interface SaveMapRatingRequest {
    rating: number;
}
