import type { Page, Request, Route } from '@playwright/test';
import type { Match, Team, TeamDashboardData, TeamEvent, TeamEventType, TeamInvite, TeamMember, TeamOffDay, TeamStats } from '../../src/types';

type Role = 'Captain' | 'Officer' | 'Player';

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
}

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
    readonly requests: RecordedRequest[] = [];
    private readonly overrides: { method: string; pattern: RegExp; handler: Handler }[] = [];
    private nextId = 1000;

    constructor(teams: MockTeam[] = [team(1, 'Tokyo Ronin')]) {
        this.teams = teams;
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

        return { status: 404, json: { message: `No mock for ${method} ${path}` } };
    }
}
