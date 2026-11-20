import { hasResult, type Match } from '../types';
import { killDeathRatio } from './match-stats';

/** Limits enforced by LineupPlayerDtoValidator. */
export const MIN_PLAYER_RATING = 0.01;
export const MAX_PLAYER_RATING = 99.99;

export type ParsedPlayerRating = { ok: true; value: number | null } | { ok: false; message: string };

const RANGE_MESSAGE = `Enter a rating from ${MIN_PLAYER_RATING} to ${MAX_PLAYER_RATING}.`;

/**
 * Reads a typed match rating: 0.01 to 99.99 with at most two decimals. A comma works as the
 * decimal point too ("1,15"), and an empty field means not rated.
 */
export function parsePlayerRating(input: string): ParsedPlayerRating {
    const text = input.trim().replace(',', '.');
    if (text === '') return { ok: true, value: null };
    if (/^\d{1,2}\.\d{3,}$/.test(text)) return { ok: false, message: 'Use at most two decimals, like 1.15.' };
    if (!/^\d{1,2}(\.\d{0,2})?$/.test(text)) return { ok: false, message: RANGE_MESSAGE };
    // Hundredths as an integer, so 1.15 stays exactly 1.15.
    const value = Math.round(Number(text) * 100) / 100;
    if (value < MIN_PLAYER_RATING || value > MAX_PLAYER_RATING) return { ok: false, message: RANGE_MESSAGE };
    return { ok: true, value };
}

/** True when every typed rating is valid (or empty). */
export const ratingsAreValid = (typed: { rating: string }[]) => typed.every(p => parsePlayerRating(p.rating).ok);

/** Always two decimals, like ratings are usually shown: "1.05", "0.90". */
export const formatPlayerRating = (value: number) => value.toFixed(2);

// ---- Player statistics ----

export type MatchTypeFilter = 'all' | 'Scrim' | 'Official';
export type PeriodFilter = '30d' | '90d' | 'year' | 'all';

export interface PlayerStatsFilters {
    type: MatchTypeFilter;
    period: PeriodFilter;
    /** A map name from the pool, or 'all'. */
    map: string;
    /** Players who played fewer matches than this are left out. */
    minMatches: number;
}

export interface PlayerStatsRow {
    userId: string;
    username: string;
    /** Finished matches the player played in the filter. */
    matches: number;
    wins: number;
    /** Wins out of matches played, 0 to 100. */
    winRate: number;
    /** Null when no match has kills and deaths. */
    killDeath: number | null;
    /** Average of the recorded ADRs, or null. */
    adr: number | null;
    /** Average of the recorded HS%, or null. */
    headshotPercentage: number | null;
    /** Rated matches in the filter. */
    rated: number;
    /** Null for a player who was never rated. */
    average: number | null;
    best: number | null;
    worst: number | null;
    /** Average of the player's last five rated matches (or fewer, if that's all there is). */
    form: number | null;
    /** Ratings oldest first, for the trend line. */
    history: number[];
}

/** How many of the latest matches the form column averages. */
export const FORM_MATCHES = 5;

const DAY_MS = 24 * 60 * 60 * 1000;

/** The earliest match date the period keeps, or null for all time. */
export function periodStart(period: PeriodFilter, now: Date): Date | null {
    switch (period) {
        case '30d': return new Date(now.getTime() - 30 * DAY_MS);
        case '90d': return new Date(now.getTime() - 90 * DAY_MS);
        case 'year': return new Date(now.getFullYear(), 0, 1);
        case 'all': return null;
    }
}

const matchDate = (match: Match) => new Date(match.playedDate ?? match.scheduledDate);

/** Finished matches the filters keep, oldest first. */
export function filterFinishedMatches(matches: Match[], filters: Omit<PlayerStatsFilters, 'minMatches'>, now: Date): Match[] {
    const start = periodStart(filters.period, now);
    return matches
        .filter(m => hasResult(m))
        .filter(m => filters.type === 'all' || m.type === filters.type)
        .filter(m => !start || matchDate(m) >= start)
        .filter(m => filters.map === 'all' || m.maps.some(map => map.mapName === filters.map))
        .sort((a, b) => matchDate(a).getTime() - matchDate(b).getTime());
}

/** True when the match has a result and at least one player who played. */
export const hasPlayedLineup = (match: Match) => hasResult(match) && match.lineup.some(p => p.played);

const mean = (values: number[]) => (values.length === 0 ? null : values.reduce((sum, v) => sum + v, 0) / values.length);

/** One row per player who played in the filtered matches. Not sorted. */
export function playerStats(matches: Match[], filters: PlayerStatsFilters, now: Date): PlayerStatsRow[] {
    interface Totals {
        username: string;
        matches: number;
        wins: number;
        kills: number;
        deaths: number;
        killDeathMatches: number;
        adr: number[];
        headshots: number[];
        ratings: number[];
    }
    const byPlayer = new Map<string, Totals>();

    for (const match of filterFinishedMatches(matches, filters, now)) {
        for (const entry of match.lineup ?? []) {
            if (!entry.played) continue;
            const player = byPlayer.get(entry.userId)
                ?? { username: entry.username, matches: 0, wins: 0, kills: 0, deaths: 0, killDeathMatches: 0, adr: [], headshots: [], ratings: [] };
            // Matches are oldest first, so the name ends up as the most recent one.
            player.username = entry.username;
            player.matches++;
            if (match.matchOutcome === 'Win') player.wins++;
            if (entry.kills != null && entry.deaths != null) {
                player.kills += entry.kills;
                player.deaths += entry.deaths;
                player.killDeathMatches++;
            }
            if (entry.adr != null) player.adr.push(entry.adr);
            if (entry.headshotPercentage != null) player.headshots.push(entry.headshotPercentage);
            if (entry.rating != null) player.ratings.push(entry.rating);
            byPlayer.set(entry.userId, player);
        }
    }

    return [...byPlayer.entries()]
        .filter(([, p]) => p.matches >= filters.minMatches)
        .map(([userId, p]) => ({
            userId,
            username: p.username,
            matches: p.matches,
            wins: p.wins,
            winRate: (p.wins / p.matches) * 100,
            killDeath: p.killDeathMatches === 0 ? null : killDeathRatio(p.kills, p.deaths),
            adr: mean(p.adr),
            headshotPercentage: mean(p.headshots),
            rated: p.ratings.length,
            average: mean(p.ratings),
            best: p.ratings.length === 0 ? null : Math.max(...p.ratings),
            worst: p.ratings.length === 0 ? null : Math.min(...p.ratings),
            form: mean(p.ratings.slice(-FORM_MATCHES)),
            history: p.ratings,
        }));
}

export type PlayerStatsSortKey =
    | 'username' | 'matches' | 'winRate' | 'killDeath' | 'adr' | 'headshotPercentage' | 'average' | 'best' | 'worst' | 'form';

export interface PlayerStatsSort {
    key: PlayerStatsSortKey;
    desc: boolean;
}

export const DEFAULT_PLAYER_SORT: PlayerStatsSort = { key: 'average', desc: true };

export function sortPlayerStats(rows: PlayerStatsRow[], sort: PlayerStatsSort): PlayerStatsRow[] {
    const direction = sort.desc ? -1 : 1;
    return [...rows].sort((a, b) => {
        let byKey: number;
        if (sort.key === 'username') {
            byKey = a.username.localeCompare(b.username) * direction;
        } else {
            const x = a[sort.key];
            const y = b[sort.key];
            // Missing values always go last.
            byKey = x == null || y == null ? (x == null ? 1 : 0) - (y == null ? 1 : 0) : (x - y) * direction;
        }
        // Ties fall back to the name, so the order never jumps around.
        return byKey !== 0 ? byKey : a.username.localeCompare(b.username);
    });
}

/** Clicking the sorted column flips it; another column starts high-to-low (names A-Z). */
export const nextPlayerSort = (current: PlayerStatsSort, key: PlayerStatsSortKey): PlayerStatsSort =>
    current.key === key ? { key, desc: !current.desc } : { key, desc: key !== 'username' };
