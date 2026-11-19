import type { LineupEntry, LineupPlayer } from '../types';

/** Limits enforced by LineupPlayerDtoValidator (PlayerMatchStats in RosterSensei.Domain). */
export const MAX_STAT_COUNT = 999;
export const MAX_ADR = 999.9;
export const MAX_HEADSHOT_PERCENTAGE = 100;

export type StatKey = 'kills' | 'deaths' | 'assists' | 'adr' | 'headshotPercentage';

export const STAT_KEYS: StatKey[] = ['kills', 'deaths', 'assists', 'adr', 'headshotPercentage'];

/** Short labels for inputs and table headers, and the full name for screen readers and hints. */
export const STAT_LABELS: Record<StatKey, { short: string; long: string }> = {
    kills: { short: 'K', long: 'Kills' },
    deaths: { short: 'D', long: 'Deaths' },
    assists: { short: 'A', long: 'Assists' },
    adr: { short: 'ADR', long: 'Average damage per round' },
    headshotPercentage: { short: 'HS%', long: 'Headshot percentage' },
};

/** A player's stats as typed in a form: empty means not recorded. */
export type TypedStats = Record<StatKey, string>;

export const EMPTY_STATS: TypedStats = { kills: '', deaths: '', assists: '', adr: '', headshotPercentage: '' };

export type ParsedStat = { ok: true; value: number | null } | { ok: false; message: string };

function parseCount(text: string, label: string): ParsedStat {
    if (text === '') return { ok: true, value: null };
    if (!/^\d{1,3}$/.test(text)) return { ok: false, message: `${label}: a whole number from 0 to ${MAX_STAT_COUNT}.` };
    return { ok: true, value: Number(text) };
}

function parseOneDecimal(text: string, label: string, max: number): ParsedStat {
    if (text === '') return { ok: true, value: null };
    const normalized = text.replace(',', '.');
    if (/^\d+\.\d{2,}$/.test(normalized)) return { ok: false, message: `${label}: use at most one decimal.` };
    const range = `${label}: from 0 to ${max}.`;
    if (!/^\d{1,3}(\.\d?)?$/.test(normalized)) return { ok: false, message: range };
    // Tenths as an integer, so 88.4 stays exactly 88.4.
    const value = Math.round(Number(normalized) * 10) / 10;
    return value > max ? { ok: false, message: range } : { ok: true, value };
}

/** Reads one typed stat. A comma works as the decimal point for ADR and HS%. */
export function parseStat(key: StatKey, input: string): ParsedStat {
    const text = input.trim();
    switch (key) {
        case 'adr': return parseOneDecimal(text, 'ADR', MAX_ADR);
        case 'headshotPercentage': return parseOneDecimal(text, 'HS%', MAX_HEADSHOT_PERCENTAGE);
        default: return parseCount(text, STAT_LABELS[key].long);
    }
}

/** The first problem with a player's typed stats, or undefined when every field is fine (or empty). */
export function statsError(stats: TypedStats): string | undefined {
    for (const key of STAT_KEYS) {
        const parsed = parseStat(key, stats[key]);
        if (!parsed.ok) return parsed.message;
    }
    return undefined;
}

/** The typed stats as request fields; empty fields are left out. Call only once statsError is undefined. */
export function statsToRequest(stats: TypedStats): Pick<LineupPlayer, StatKey> {
    const request: Pick<LineupPlayer, StatKey> = {};
    for (const key of STAT_KEYS) {
        const parsed = parseStat(key, stats[key]);
        if (parsed.ok && parsed.value !== null) request[key] = parsed.value;
    }
    return request;
}

/** A lineup entry's stats as the form shows them. */
export function statsToTyped(entry: Partial<Pick<LineupEntry, StatKey>> | undefined): TypedStats {
    const text = (value: number | null | undefined, decimals: number) => (value == null ? '' : value.toFixed(decimals));
    return {
        kills: text(entry?.kills, 0),
        deaths: text(entry?.deaths, 0),
        assists: text(entry?.assists, 0),
        adr: text(entry?.adr, 1),
        headshotPercentage: text(entry?.headshotPercentage, 1),
    };
}

/** The saved stats of an entry, ready to send back unchanged. */
export function statsOf(entry: Partial<Pick<LineupEntry, StatKey>> | undefined): Pick<LineupPlayer, StatKey> {
    const kept: Pick<LineupPlayer, StatKey> = {};
    for (const key of STAT_KEYS) {
        const value = entry?.[key];
        if (value != null) kept[key] = value;
    }
    return kept;
}

export const hasStats = (entry: Pick<LineupEntry, 'rating'> & Partial<Pick<LineupEntry, StatKey>>) =>
    entry.rating != null || STAT_KEYS.some(key => entry[key] != null);

/** Kills per death. No deaths counts as one, so a flawless line still reads as a number. */
export const killDeathRatio = (kills: number, deaths: number) => kills / Math.max(deaths, 1);

/** "1.25", "0.84". */
export const formatRatio = (value: number) => value.toFixed(2);

/** "88.4". */
export const formatOneDecimal = (value: number) => value.toFixed(1);

/** "+6", "-3", "0". */
export const formatDifference = (value: number) => (value > 0 ? `+${value}` : String(value));

export interface TeamTotals {
    /** Players who played and have at least one value recorded. */
    players: number;
    kills: number | null;
    deaths: number | null;
    assists: number | null;
    /** Team kills per team death, from players with both recorded. */
    killDeath: number | null;
    /** Mean of the players' ADR. */
    adr: number | null;
    /** Mean of the players' HS%. */
    headshotPercentage: number | null;
    /** Mean of the players' ratings. */
    rating: number | null;
}

const sum = (values: number[]) => (values.length === 0 ? null : values.reduce((total, v) => total + v, 0));
const mean = (values: number[]) => (values.length === 0 ? null : values.reduce((total, v) => total + v, 0) / values.length);
const recorded = (lineup: LineupEntry[], key: StatKey | 'rating') =>
    lineup.map(p => p[key]).filter((v): v is number => v != null);

/** The team's line for a match, added up from the players who played. Null where nobody has a value. */
export function teamTotals(lineup: LineupEntry[]): TeamTotals {
    const played = lineup.filter(p => p.played);
    const withBoth = played.filter(p => p.kills != null && p.deaths != null);
    return {
        players: played.filter(hasStats).length,
        kills: sum(recorded(played, 'kills')),
        deaths: sum(recorded(played, 'deaths')),
        assists: sum(recorded(played, 'assists')),
        killDeath: withBoth.length === 0
            ? null
            : killDeathRatio(withBoth.reduce((t, p) => t + p.kills!, 0), withBoth.reduce((t, p) => t + p.deaths!, 0)),
        adr: mean(recorded(played, 'adr')),
        headshotPercentage: mean(recorded(played, 'headshotPercentage')),
        rating: mean(recorded(played, 'rating')),
    };
}
