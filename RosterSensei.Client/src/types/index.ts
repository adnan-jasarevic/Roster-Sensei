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
export type TeamRole = 'Captain' | 'Officer' | 'Player' | 'Coach';

/** Every role the Owner can give a member, in the order the role picker lists them. */
export const TEAM_ROLES: readonly TeamRole[] = ['Player', 'Officer', 'Captain', 'Coach'];

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
 * Editing a match (PUT /Matches/{teamId}/{matchId}) can move it to any other status.
 */
export type MatchStatusName = 'Scheduled' | 'Finished' | 'Cancelled';

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
    /** "13 - 9" once the match is finished (`hasResult`), null before that. */
    finalScore: string | null;
    bestOf?: number;
    maps: MatchMap[];
    /** The result once the match is finished (`hasResult`), null before that. */
    matchOutcome: MatchOutcome | null;
    notes?: string;
    /** Who was in the lineup, players who played first. Empty when it wasn't recorded. */
    lineup: LineupEntry[];
    /** Who coached. Empty when nobody did, or it wasn't recorded. */
    coaches?: MatchCoachEntry[];
    /** Orders (1, 2, ...) of the maps whose stats came from a demo. */
    importedMapOrders?: number[];
    /** Sent back with an edit, so a save over someone else's newer changes is refused (409). */
    version: number;
}

/** A coach of a match: a team member (userId) or someone from outside the team. */
export interface MatchCoachEntry {
    userId: string | null;
    /** The member's username, or the name entered for a coach from outside the team. */
    displayName: string;
    steamId: string | null;
}

/** A coach sent with a match or a demo import. */
export interface MatchCoachRequest {
    userId?: string;
    name?: string;
    steamId?: string;
}

/** What the browser read from one map's demo: the game's own end-of-match scoreboard. */
export interface DemoSummary {
    parserVersion: string;
    patchVersion: number | null;
    /** SHA-256 of the unpacked demo, lowercase hex. */
    fingerprint: string;
    /** As the demo names it, e.g. "de_mirage". */
    mapName: string;
    rounds: number;
    /** Always two, in no particular order. */
    sides: DemoSide[];
}

export interface DemoSide {
    score: number;
    firstHalf: number;
    secondHalf: number;
    players: DemoPlayer[];
    coaches: { steamId: string; name: string }[];
}

export interface DemoPlayer {
    steamId: string;
    name: string;
    kills: number;
    deaths: number;
    assists: number;
    damage: number;
    headshotKills: number;
}

export interface DemoImportStatus {
    paused: boolean;
    message: string | null;
}

/** What an import would do, from POST /DemoImport/{teamId}/{matchId}/preview. */
export interface DemoImportPreview {
    /** 0 or 1: the side with the most team members. Null when nobody from the team is in the demo. */
    ourSide: number | null;
    /** The map of the match this demo would fill; one past the last map adds a map. */
    mapOrder: number;
    replacesImport: boolean;
    sides: {
        score: number;
        firstHalf: number;
        secondHalf: number;
        players: (Omit<DemoPlayer, 'damage' | 'headshotKills'> & { userId: string | null; username: string | null; canPlay: boolean; adr: number; headshotPercentage: number })[];
        coaches: { steamId: string; name: string; userId: string | null; username: string | null }[];
    }[];
    /** The saved coaches plus the detected side's coaches. */
    coaches: MatchCoachEntry[];
}

export interface ApplyDemoImportRequest {
    demo: DemoSummary;
    ourSide: number;
    mapOrder: number;
    players: { steamId: string; userId: string | null }[];
    coaches: MatchCoachRequest[];
    version: number;
}

/** One player in a match lineup, as the API returns it. */
export interface LineupEntry {
    userId: string;
    username: string;
    /** Free text such as "AWP" or "IGL". Null when not recorded. */
    role: string | null;
    /** False for a substitute who was named but didn't play. */
    played: boolean;
    /** 0.01 to 99.99. Null when not rated; only players who played in a finished match can be. */
    rating: number | null;
    /** Totals for the whole match. Null (or missing) when not recorded. */
    kills?: number | null;
    deaths?: number | null;
    assists?: number | null;
    /** Average damage per round, one decimal. */
    adr?: number | null;
    /** 0 to 100, one decimal. */
    headshotPercentage?: number | null;
}

/** One player sent in a lineup: by the Lineup dialog, or with the result in Add Match. */
export interface LineupPlayer {
    userId: string;
    role?: string;
    played: boolean;
    rating?: number;
    kills?: number;
    deaths?: number;
    assists?: number;
    adr?: number;
    headshotPercentage?: number;
}

/** Body of PUT /Matches/{teamId}/{matchId}/lineup. Replaces the whole lineup; an empty list clears it. */
export interface SetLineupRequest {
    players: LineupPlayer[];
}

/** Limits enforced by SetMatchLineupDtoValidator. */
export const LINEUP_MAX_PLAYERS = 10;
export const LINEUP_MAX_ROLE_LENGTH = 30;

/** The signed-in user's linked Steam account. steamId and profileUrl are null when none is linked. */
export interface SteamAccount {
    steamId: string | null;
    profileUrl: string | null;
    /** True when linked by signing in through Steam; false for links pasted before sign-in existed. */
    verified: boolean;
    /** Public Steam name and avatar, when Steam answered the server's lookup. */
    personaName?: string | null;
    avatarUrl?: string | null;
}

/** POST /Account/steam/sign-in: the Steam page to send the browser to. */
export interface SteamSignInStarted {
    redirectUrl: string;
}

export interface MatchResponse {
    id: number;
    opponentName: string;
    scheduledDate: string;
    playedDate?: string;
    status: MatchStatusName;
    /** Null until the match is finished. */
    finalScore?: string | null;
    matchOutcome?: MatchOutcome | null;
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
    /** True when the member proved the Steam account is theirs by signing in through Steam. */
    steamVerified?: boolean;
    /** Public Steam name and avatar, when Steam answered the server's lookup. */
    steamPersonaName?: string | null;
    steamAvatarUrl?: string | null;
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


// Numeric values match the MatchStatus enum in RosterSensei.Domain. 1 was "Live", which was removed.
export const MatchStatus = {
    Scheduled: 0,
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
    /** Who played, with optional ratings. Ratings only when the status is Finished. On an edit, leaving it out keeps the lineup. */
    lineup?: LineupPlayer[];
}

/** Body of PUT /Matches/{teamId}/{matchId}: the whole match, with the version the edit started from. */
export interface UpdateMatchRequest extends CreateMatchRequest {
    version: number;
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

// Everyone but Players (and the Owner, whatever their role) can invite and manage matches and the calendar.
export const canManageTeam = (team?: Team) =>
    !!team && (team.isOwner || team.currentUserRole === 'Captain' || team.currentUserRole === 'Officer' || team.currentUserRole === 'Coach');

// How the user's place in a team is shown, for example "Owner, Captain".
export const roleLabel = (team: Team) => team.isOwner ? `Owner, ${team.currentUserRole}` : team.currentUserRole;

// Ownership goes only to an Officer or a Coach, as in the API.
export const canReceiveOwnership = (member: TeamMember) =>
    !member.isOwner && (member.role === 'Officer' || member.role === 'Coach');

// Members who can be picked for a lineup: everyone but Coaches, plus anyone already in it.
export const lineupCandidates = (members: TeamMember[], alreadyInLineup: ReadonlySet<string>) =>
    members.filter(member => member.role !== 'Coach' || alreadyInLineup.has(member.id));

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
