import type { Page, Request, Route } from '@playwright/test';
import type { LineupEntry, LineupPlayer, MapPool, MapRating, MapSide, Match, MyAvailability, SteamAccount, Team, TeamAvailability, TeamDashboardData, TeamEvent, TeamEventType, TeamInvite, TeamMember, TeamOffDay, TeamStats } from '../../src/types';

type Role = 'Captain' | 'Officer' | 'Player' | 'Coach';

export interface MockTeam {
    id: number;
    name: string;
    /** The signed-in user's role in this team. */
    role: Role;
    isOwner: boolean;
    members: TeamMember[];
    matches: Match[];
    events: TeamEvent[];
    eventTypes: TeamEventType[];
    offDays: TeamOffDay[];
    mapRatings: MapRating[];
    /** Who is free when (UTC half hours), as GET /teams/{id}/availability returns it. */
    availability: TeamAvailability['slots'];
}

/** A day of availability free from start to end ("HH:mm", local to whoever saved it). */
export function freeDay(start: string, end: string) {
    const slot = (time: string) => { const [h, m] = time.split(':').map(Number); return h * 2 + (m >= 30 ? 1 : 0); };
    return Array.from({ length: 48 }, (_, i) => (i >= slot(start) && i < slot(end) ? '1' : '0')).join('');
}

/** Every half hour from start (inclusive) to end (exclusive), with these people free. */
export function freeSlots(start: string, end: string, userIds: string[]): TeamAvailability['slots'] {
    const slots: TeamAvailability['slots'] = [];
    for (let t = new Date(start).getTime(); t < new Date(end).getTime(); t += 30 * 60 * 1000) {
        slots.push({ start: new Date(t).toISOString(), userIds });
    }
    return slots;
}

/** Rating and scoreboard stats as the API returns them: null when not sent. */
const savedStats = (p: Partial<Omit<LineupPlayer, 'role'>>) => ({
    rating: p.rating ?? null,
    kills: p.kills ?? null,
    deaths: p.deaths ?? null,
    assists: p.assists ?? null,
    adr: p.adr ?? null,
    headshotPercentage: p.headshotPercentage ?? null,
});

/** The maps the API's map pool lists, in its order. */
export const MAP_POOL_MAPS = ['Ancient', 'Anubis', 'Dust2', 'Inferno', 'Mirage', 'Nuke', 'Overpass', 'Train', 'Vertigo'];

/** The premade types a new team starts with, ids 1-5 (Other is 5). */
export function defaultEventTypes(): TeamEventType[] {
    return [
        { id: 1, name: 'Pracc', color: '#3b82f6', preset: 'Pracc', isLocked: false },
        { id: 2, name: 'Official Match', color: '#ef4444', preset: 'OfficialMatch', isLocked: false },
        { id: 3, name: 'VOD Review', color: '#a855f7', preset: 'VodReview', isLocked: false },
        { id: 4, name: 'Theory', color: '#22c55e', preset: 'Theory', isLocked: false },
        { id: 5, name: 'Other', color: '#6b7280', preset: 'Other', isLocked: true },
    ];
}

export function teamEvent(id: number, title: string, startTime: string, endTime: string, extra: Partial<TeamEvent> = {}): TeamEvent {
    return { id, teamId: 1, title, description: null, startTime, endTime, eventTypeId: 1, version: 1, ...extra };
}

export interface RecordedRequest {
    method: string;
    path: string;
    body: unknown;
}

type Handler = (request: Request, match: RegExpMatchArray) => Promise<MockResponse | undefined> | MockResponse | undefined;

export interface MockResponse {
    status?: number;
    json?: unknown;
}

/** A response the test releases when it's ready, to hold a request "in flight". */
export interface Deferred {
    release: (response?: MockResponse) => void;
    /** Resolves once the app has sent the request. */
    requested: Promise<void>;
}

export const ME = { id: 'me', username: 'sensei', email: 'sensei@example.com' };

export function member(id: string, role: Role, extra: Partial<TeamMember> = {}): TeamMember {
    return {
        id,
        username: id,
        email: `${id}@example.com`,
        role,
        isOwner: false,
        joinedAt: '2026-10-01T18:00:00Z',
        ...extra,
    };
}

export function team(id: number, name: string, role: Role = 'Captain', isOwner = true, extra: Partial<MockTeam> = {}): MockTeam {
    return {
        id,
        name,
        role,
        isOwner,
        members: [
            member(ME.id, role, { username: ME.username, email: ME.email, isOwner }),
            ...(isOwner ? [] : [member('boss', 'Captain' === role ? 'Officer' : 'Captain', { isOwner: true })]),
            member('kenji', 'Player'),
            member('mika', 'Officer'),
        ],
        matches: [],
        events: [],
        eventTypes: defaultEventTypes(),
        offDays: [],
        mapRatings: [],
        availability: [],
        ...extra,
    };
}

export function finishedMatch(id: number, opponentName: string, ourScore: number, opponentScore: number, date: string): Match {
    return {
        id,
        opponentName,
        scheduledDate: date,
        playedDate: date,
        status: 'Finished',
        type: 'Official',
        finalScore: `${ourScore} - ${opponentScore}`,
        bestOf: 1,
        maps: [{ mapName: 'Mirage', ourScore, opponentScore }],
        matchOutcome: ourScore > opponentScore ? 'Win' : ourScore < opponentScore ? 'Loss' : 'Draw',
        lineup: [],
        version: 1,
    };
}

/**
 * An in-memory stand-in for the Roster Sensei API. It answers every request to /api the way the
 * real controllers do for the data it holds, records what the app sent, and lets a test replace
 * any endpoint (to fail it, delay it, or hold it until the test says so).
 */
export class MockApi {
    teams: MockTeam[];
    invites: TeamInvite[] = [];
    /** The signed-in user's linked SteamID64, or null. */
    steamId: string | null = null;
    /** Whether that link was made by signing in through Steam. */
    steamVerified = true;
    /** Public Steam name and avatar the server would add, when it has an API key. */
    steamProfile: { personaName: string; avatarUrl: string | null } | null = null;
    /** The SteamID64 the next Steam sign-in proves, as Steam's own check would. */
    steamSignInAs = '76561198012345678';
    /** The signed-in user's own availability. */
    myAvailability: MyAvailability = { timeZone: null, weekly: Array.from({ length: 7 }, () => '0'.repeat(48)), overrides: [] };
    readonly requests: RecordedRequest[] = [];
    private readonly overrides: { method: string; pattern: RegExp; handler: Handler }[] = [];
    private nextId = 1000;

    constructor(teams: MockTeam[] = [team(1, 'Tokyo Ronin')]) {
        this.teams = teams;
    }

    steamAccount(): SteamAccount {
        return {
            steamId: this.steamId,
            profileUrl: this.steamId ? `https://steamcommunity.com/profiles/${this.steamId}` : null,
            verified: !!this.steamId && this.steamVerified,
            personaName: this.steamId ? this.steamProfile?.personaName ?? null : null,
            avatarUrl: this.steamId ? this.steamProfile?.avatarUrl ?? null : null,
        };
    }

    /** Replaces an endpoint. The newest override wins; return undefined to fall through. */
    on(method: string, pattern: RegExp, handler: Handler) {
        this.overrides.unshift({ method, pattern, handler });
    }

    /** Answers the next matching request only when release() is called. */
    hold(method: string, pattern: RegExp): Deferred {
        let release!: (response?: MockResponse) => void;
        let markRequested!: () => void;
        const requested = new Promise<void>(resolve => { markRequested = resolve; });
        const released = new Promise<MockResponse | undefined>(resolve => { release = resolve; });
        let used = false;
        this.on(method, pattern, async request => {
            if (used) return undefined;
            used = true;
            markRequested();
            return (await released) ?? this.handleDefault(request);
        });
        return { release, requested };
    }

    /** Makes the endpoint fail with the given status until the returned function is called. */
    fail(method: string, pattern: RegExp, status = 500, message?: string) {
        let active = true;
        this.on(method, pattern, () => active ? { status, json: message ? { message } : undefined } : undefined);
        return () => { active = false; };
    }

    /**
     * Plays Steam's sign-in page: sends the browser straight back to the app's return URL with an
     * OpenID reply, or a cancel when `cancel` is set. Returns the URLs Steam was opened with.
     */
    async fakeSteam(page: Page, { cancel = false } = {}) {
        const opened: string[] = [];
        await page.route('https://steamcommunity.com/openid/**', async route => {
            const url = new URL(route.request().url());
            opened.push(url.toString());
            const back = new URL(url.searchParams.get('openid.return_to')!);
            back.searchParams.set('openid.ns', 'http://specs.openid.net/auth/2.0');
            back.searchParams.set('openid.mode', cancel ? 'cancel' : 'id_res');
            if (!cancel) {
                back.searchParams.set('openid.return_to', url.searchParams.get('openid.return_to')!);
                back.searchParams.set('openid.claimed_id', `https://steamcommunity.com/openid/id/${this.steamSignInAs}`);
                back.searchParams.set('openid.sig', 'c2ln');
            }
            await route.fulfill({ status: 302, headers: { location: back.toString() } });
        });
        return opened;
    }

    requestsTo(method: string, pattern: RegExp) {
        return this.requests.filter(r => r.method === method && pattern.test(r.path));
    }

    async install(page: Page, { signedIn = true } = {}) {
        await page.route(/\/api\//, route => this.route(route));
        if (signedIn) {
            // Only on the first page load, so a test can still see the app remove the token.
            await page.addInitScript(() => {
                if (sessionStorage.getItem('e2e-signed-in')) return;
                sessionStorage.setItem('e2e-signed-in', '1');
                localStorage.setItem('jwt_token', 'test-token');
            });
        }
    }

    private async route(route: Route) {
        const request = route.request();
        const url = new URL(request.url());
        const path = url.pathname.replace(/^\/api/, '') + url.search;
        const method = request.method();
        let body: unknown;
        try {
            body = request.postDataJSON();
        } catch {
            body = request.postData();
        }
        this.requests.push({ method, path, body });

        let response: MockResponse | undefined;
        for (const override of this.overrides) {
            const match = path.match(override.pattern);
            if (override.method === method && match) {
                response = await override.handler(request, match);
                if (response) break;
            }
        }
        response ??= this.handleDefault(request);

        // The page may have moved on (aborted request, closed page); that's fine.
        await route.fulfill({
            status: response.status ?? 200,
            contentType: 'application/json',
            body: response.json === undefined ? '' : JSON.stringify(response.json),
        }).catch(() => undefined);
    }

    private teamDto(t: MockTeam): Team {
        return { id: t.id, name: t.name, createdAt: '2026-09-01T12:00:00Z', currentUserRole: t.role, isOwner: t.isOwner };
    }

    private findTeam(id: string | number) {
        return this.teams.find(t => t.id === Number(id));
    }

    private handleDefault(request: Request): MockResponse {
        const url = new URL(request.url());
        const path = url.pathname.replace(/^\/api/, '');
        const method = request.method();
        const body = (() => { try { return request.postDataJSON(); } catch { return undefined; } })();
        let m: RegExpMatchArray | null;

        if (method === 'POST' && path === '/Auth/login') {
            return body?.password === 'correct-horse'
                ? { json: { username: ME.username, token: 'test-token' } }
                : { status: 401, json: { message: 'Invalid email or password' } };
        }

        if (method === 'GET' && path === '/Teams/my-teams') return { json: this.teams.map(t => this.teamDto(t)) };
        if (method === 'GET' && path === '/Teams/my-invites') return { json: this.invites };

        if (method === 'POST' && path === '/Teams') {
            const created = team(this.nextId++, String(body?.name ?? ''), 'Captain', true);
            this.teams.push(created);
            return { json: this.teamDto(created) };
        }

        if ((m = path.match(/^\/Teams\/(\d+)\/dashboard$/)) && method === 'GET') {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            const finished = t.matches.filter(x => x.status === 'Finished');
            const upcoming = t.matches.filter(x => x.status === 'Scheduled');
            const wins = finished.filter(x => x.matchOutcome === 'Win').length;
            const data: TeamDashboardData = {
                totalMatches: finished.length,
                winRatePercentage: finished.length ? Math.round((wins / finished.length) * 100) : 0,
                totalMembers: t.members.length,
                upcomingMatchesCount: upcoming.length,
                recentMatches: finished,
                upcomingMatches: upcoming,
            };
            return { json: data };
        }

        if ((m = path.match(/^\/Teams\/(\d+)\/members$/))) {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            if (method === 'GET') return { json: t.members };
            if (method === 'POST') return { json: { message: `Invite sent to ${body?.userEmail}.` } };
        }

        if ((m = path.match(/^\/Teams\/(\d+)\/members\/([^/]+)$/)) && method === 'DELETE') {
            const t = this.findTeam(m[1]);
            if (t) t.members = t.members.filter(x => x.id !== m![2]);
            return { json: { message: 'Member removed.' } };
        }

        if ((m = path.match(/^\/Teams\/(\d+)\/members\/([^/]+)\/role$/)) && method === 'PUT') {
            const t = this.findTeam(m[1]);
            const target = t?.members.find(x => x.id === m![2]);
            if (target) target.role = body?.newRole;
            return { json: { message: 'Role updated.' } };
        }

        if ((m = path.match(/^\/Teams\/(\d+)\/leave$/)) && method === 'POST') {
            this.teams = this.teams.filter(t => t.id !== Number(m![1]));
            return { json: { message: 'You left the team.' } };
        }

        if ((m = path.match(/^\/Teams\/(\d+)$/)) && method === 'DELETE') {
            this.teams = this.teams.filter(t => t.id !== Number(m![1]));
            return { json: { message: 'Team deleted.' } };
        }

        if ((m = path.match(/^\/Matches\/(\d+)\/stats$/)) && method === 'GET') {
            const t = this.findTeam(m[1]);
            const finished = t?.matches.filter(x => x.status === 'Finished') ?? [];
            const stats: TeamStats = { matchesPlayed: finished.length, mapsPlayed: finished.length, maps: [], form: [] };
            return { json: stats };
        }

        if ((m = path.match(/^\/Matches\/(\d+)$/))) {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            if (method === 'GET') return { json: t.matches };
            if (method === 'POST') return { json: { id: this.nextId++ } };
        }

        if ((m = path.match(/^\/Matches\/(\d+)\/(\d+)$/)) && method === 'PUT') {
            const t = this.findTeam(m[1]);
            const target = t?.matches.find(x => x.id === Number(m![2]));
            if (!t || !target) return { status: 404, json: { message: 'Match was not found.' } };
            if (body?.version !== target.version) {
                return { status: 409, json: { message: 'Someone else changed this match while you were editing it. Open it again to see their changes.' } };
            }
            const status = (['Scheduled', undefined, 'Finished', 'Cancelled'] as const)[body.status as number]!;
            const maps = (body.maps ?? []) as Match['maps'];
            const played = maps.filter(x => x.ourScore != null && x.opponentScore != null);
            const [ours, theirs] = played.length === 0
                ? [body.ourScore ?? 0, body.opponentScore ?? 0]
                : played.length === 1 && (body.bestOf ?? 1) === 1
                    ? [played[0].ourScore!, played[0].opponentScore!]
                    : [played.filter(x => x.ourScore! > x.opponentScore!).length, played.filter(x => x.opponentScore! > x.ourScore!).length];
            const lineup: LineupEntry[] = body.lineup
                ? (body.lineup as LineupPlayer[]).map(p => ({
                    userId: p.userId,
                    username: t.members.find(x => x.id === p.userId)?.username ?? target.lineup.find(x => x.userId === p.userId)?.username ?? p.userId,
                    role: p.role ?? null,
                    played: p.played,
                    ...savedStats(p),
                }))
                : target.lineup.map(p => (status === 'Finished' ? p : { ...p, ...savedStats({}) }));
            Object.assign(target, {
                opponentName: body.opponentName,
                tournament: body.tournament,
                scheduledDate: body.scheduledDate,
                playedDate: status === 'Finished' ? body.playedDate : undefined,
                status,
                type: body.type === 1 ? 'Scrim' : 'Official',
                bestOf: body.bestOf,
                maps,
                notes: body.notes,
                // Like the API: no score or outcome until the match is finished.
                finalScore: status === 'Finished' ? `${ours} - ${theirs}` : null,
                matchOutcome: status !== 'Finished' ? null : ours > theirs ? 'Win' : ours < theirs ? 'Loss' : 'Draw',
                lineup,
                version: target.version + 1,
            } satisfies Partial<Match>);
            return { json: target };
        }

        if ((m = path.match(/^\/Matches\/(\d+)\/(\d+)\/lineup$/)) && method === 'PUT') {
            const t = this.findTeam(m[1]);
            const target = t?.matches.find(x => x.id === Number(m![2]));
            if (!t || !target) return { status: 404, json: { message: 'Match was not found.' } };
            const players = (body?.players ?? []) as (Omit<LineupPlayer, 'role'> & { role?: string | null })[];
            if (players.some(p => !t.members.some(x => x.id === p.userId))) {
                return { status: 400, json: { message: 'Every player in the lineup must be a member of the team.' } };
            }
            const lineup: LineupEntry[] = players
                .map(p => ({
                    userId: p.userId,
                    username: t.members.find(x => x.id === p.userId)!.username,
                    role: p.role?.trim() || null,
                    played: p.played,
                    ...savedStats(p),
                }))
                .sort((a, b) => Number(b.played) - Number(a.played));
            target.lineup = lineup;
            return { json: lineup };
        }

        if ((m = path.match(/^\/teams\/(\d+)\/map-pool$/i)) && method === 'GET') {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 403, json: { message: "You don't have access to the map pool of this team." } };
            const pool: MapPool = {
                maps: MAP_POOL_MAPS,
                players: t.members.map(x => ({ userId: x.id, username: x.username })),
                currentUserId: ME.id,
                ratings: t.mapRatings,
            };
            return { json: pool };
        }

        // Like the API, the rating written is always the signed-in user's own.
        if ((m = path.match(/^\/teams\/(\d+)\/map-pool\/([^/]+)\/([^/]+)$/i)) && (method === 'PUT' || method === 'DELETE')) {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 403, json: { message: "You don't have access to the map pool of this team." } };
            const side = m[2].toUpperCase() as MapSide;
            const mapName = MAP_POOL_MAPS.find(x => x.toLowerCase() === decodeURIComponent(m![3]).toLowerCase());
            if ((side !== 'T' && side !== 'CT') || !mapName) return { status: 404, json: { message: "That map isn't in the map pool." } };
            const rating = Number(body?.rating);
            if (method === 'PUT' && (!(rating >= 1 && rating <= 10) || Math.round(rating * 10) !== rating * 10)) {
                return { status: 400, json: { errors: { Rating: ['A rating must be from 1 to 10.'] } } };
            }
            const isCell = (r: MapRating) => r.userId === ME.id && r.side === side && r.mapName === mapName;
            t.mapRatings = t.mapRatings.filter(r => !isCell(r));
            if (method === 'DELETE') return { json: { message: 'Rating cleared.' } };
            const saved: MapRating = { userId: ME.id, mapName, side, rating };
            t.mapRatings.push(saved);
            return { json: saved };
        }

        if (path === '/Account/steam') {
            if (method === 'GET') return { json: this.steamAccount() };
            if (method === 'DELETE') {
                this.steamId = null;
                this.steamVerified = false;
                return { json: { message: 'Steam account unlinked.' } };
            }
        }

        if (path === '/Account/steam/sign-in' && method === 'POST') {
            // The real API checks the return URL's origin and adds a one-time state to it.
            const returnTo = `${String(body?.returnUrl)}?rs_state=STATE`;
            return { json: { redirectUrl: `https://steamcommunity.com/openid/login?openid.mode=checkid_setup&openid.return_to=${encodeURIComponent(returnTo)}` } };
        }

        if (path === '/Account/steam/sign-in/complete' && method === 'POST') {
            const parameters = (body?.parameters ?? {}) as Record<string, string>;
            if (parameters['openid.mode'] === 'cancel') return { status: 400, json: { message: 'Steam sign-in was cancelled.' } };
            if (!String(parameters['openid.return_to'] ?? '').includes('rs_state=STATE')) {
                return { status: 400, json: { message: 'This Steam sign-in expired or was already used. Please sign in through Steam again.' } };
            }
            this.steamId = this.steamSignInAs;
            this.steamVerified = true;
            return { json: this.steamAccount() };
        }

        if ((m = path.match(/^\/teams\/(\d+)\/events$/i))) {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            if (method === 'GET') {
                const from = new Date(url.searchParams.get('from') ?? 0);
                const to = new Date(url.searchParams.get('to') ?? 0);
                return { json: t.events.filter(e => new Date(e.startTime) < to && new Date(e.endTime) > from) };
            }
            if (method === 'POST') {
                const created: TeamEvent = { id: this.nextId++, teamId: t.id, title: String(body?.title ?? ''), description: body?.description ?? null, startTime: body?.startTime, endTime: body?.endTime, eventTypeId: body?.eventTypeId, version: 1 };
                t.events.push(created);
                return { json: created };
            }
        }

        if ((m = path.match(/^\/teams\/(\d+)\/events\/(\d+)$/i))) {
            const t = this.findTeam(m[1]);
            const index = t?.events.findIndex(e => e.id === Number(m![2])) ?? -1;
            if (!t || index < 0) return { status: 404, json: { message: 'Event was not found.' } };
            if (method === 'PUT') {
                const current = t.events[index];
                if (body?.version !== current.version) return { status: 409, json: { message: 'Someone else changed this event while you were editing it. Open it again to see their changes.' } };
                const updated: TeamEvent = { ...current, title: String(body?.title ?? ''), description: body?.description ?? null, startTime: body?.startTime, endTime: body?.endTime, eventTypeId: body?.eventTypeId, version: current.version + 1 };
                t.events[index] = updated;
                return { json: updated };
            }
            if (method === 'DELETE') {
                t.events.splice(index, 1);
                return { json: { message: 'Event deleted.' } };
            }
        }

        if ((m = path.match(/^\/teams\/(\d+)\/event-types$/i)) && method === 'GET') {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            return { json: { types: t.eventTypes, availablePresets: [] } };
        }

        if ((m = path.match(/^\/teams\/(\d+)\/off-days$/i)) && method === 'GET') {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            return { json: t.offDays };
        }

        if ((m = path.match(/^\/teams\/(\d+)\/off-days\/(\d{4}-\d{2}-\d{2})$/i))) {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            const date = m[2];
            if (method === 'PUT') {
                // Marking a day that is already off is not an error, as in the API.
                const offDay: TeamOffDay = { date, startTime: body?.startTime, endTime: body?.endTime };
                t.offDays = [...t.offDays.filter(d => d.date !== date), offDay];
                return { json: offDay };
            }
            if (method === 'DELETE') {
                if (!t.offDays.some(d => d.date === date)) return { status: 404, json: { message: "That day isn't an off day." } };
                t.offDays = t.offDays.filter(d => d.date !== date);
                return { json: { message: 'The day is no longer an off day.' } };
            }
        }

        if (path === '/availability' && method === 'GET') return { json: this.myAvailability };

        if (path === '/availability/weekly' && method === 'PUT') {
            this.myAvailability = { ...this.myAvailability, timeZone: body?.timeZone, weekly: body?.weekly };
            return { json: this.myAvailability };
        }

        if ((m = path.match(/^\/availability\/days\/(\d{4}-\d{2}-\d{2})$/i))) {
            const date = m[1];
            const others = this.myAvailability.overrides.filter(o => o.date !== date);
            if (method === 'PUT') {
                this.myAvailability = { ...this.myAvailability, timeZone: this.myAvailability.timeZone ?? body?.timeZone, overrides: [...others, { date, slots: body?.slots }] };
                return { json: { date, slots: body?.slots } };
            }
            if (method === 'DELETE') {
                if (others.length === this.myAvailability.overrides.length) return { status: 404, json: { message: 'That day already follows your usual week.' } };
                this.myAvailability = { ...this.myAvailability, overrides: others };
                return { json: { message: 'The day follows your usual week again.' } };
            }
        }

        if ((m = path.match(/^\/teams\/(\d+)\/availability$/i)) && method === 'GET') {
            const t = this.findTeam(m[1]);
            if (!t) return { status: 404, json: { message: 'Team not found.' } };
            const from = new Date(url.searchParams.get('from') ?? 0).getTime();
            const to = new Date(url.searchParams.get('to') ?? 0).getTime();
            const slots = t.availability.filter(s => new Date(s.start).getTime() >= from && new Date(s.start).getTime() < to);
            const meFilledIn = this.myAvailability.weekly.some(d => d.includes('1')) || this.myAvailability.overrides.some(o => o.slots.includes('1'));
            const result: TeamAvailability = {
                slotMinutes: 30,
                playersNeeded: 5,
                members: t.members.map(mb => ({
                    userId: mb.id,
                    username: mb.username,
                    role: mb.role,
                    countsTowardsLineup: mb.role !== 'Coach',
                    hasFilledIn: mb.id === ME.id ? meFilledIn : t.availability.some(s => s.userIds.includes(mb.id)),
                })),
                slots,
            };
            return { json: result };
        }

        return { status: 404, json: { message: `No mock for ${method} ${path}` } };
    }
}
