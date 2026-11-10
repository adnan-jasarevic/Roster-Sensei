import { type MapRating, type MapSide } from '../types';

/** Limits enforced by SaveMapRatingDtoValidator. */
export const MIN_RATING = 1;
export const MAX_RATING = 10;

export const SIDES: MapSide[] = ['T', 'CT'];

export type ParsedRating = { ok: true; value: number | null } | { ok: false; message: string };

/**
 * Reads what a player typed into a rating cell: 1 to 10 with at most one decimal. A comma works as
 * the decimal point too ("6,5"), and an empty cell clears the rating.
 */
export function parseRating(input: string): ParsedRating {
    const text = input.trim().replace(',', '.');
    if (text === '') return { ok: true, value: null };
    if (!/^\d{1,2}(\.\d?)?$/.test(text)) {
        return /^\d+\.\d{2,}$/.test(text)
            ? { ok: false, message: 'Use at most one decimal, like 6.5.' }
            : { ok: false, message: `Enter a number from ${MIN_RATING} to ${MAX_RATING}.` };
    }
    // Tenths as an integer, so 6.5 stays exactly 6.5.
    const value = Math.round(Number(text) * 10) / 10;
    if (value < MIN_RATING || value > MAX_RATING) return { ok: false, message: `Enter a number from ${MIN_RATING} to ${MAX_RATING}.` };
    return { ok: true, value };
}

/** "7" or "6.5": whole numbers without a trailing ".0", like people type them. */
export const formatRating = (value: number) => (Number.isInteger(value) ? String(value) : value.toFixed(1));

/** The average of the ratings given, or null when nobody has rated it yet. */
export function average(values: (number | null | undefined)[]): number | null {
    const given = values.filter((v): v is number => typeof v === 'number');
    if (given.length === 0) return null;
    return given.reduce((sum, v) => sum + v, 0) / given.length;
}

export const cellKey = (side: MapSide, mapName: string, userId: string) => `${side}|${mapName}|${userId}`;

export const ratingsByCell = (ratings: MapRating[]) =>
    new Map(ratings.map(r => [cellKey(r.side, r.mapName, r.userId), r.rating]));

/** What a table is sorted by: the map name, the result, or one player's column. */
export type SortKey = 'map' | 'result' | `player:${string}`;

export interface SortState {
    key: SortKey;
    desc: boolean;
}

export const DEFAULT_SORT: SortState = { key: 'map', desc: false };

export interface PoolRow {
    map: string;
    /** Rating per player, by user id. */
    ratings: Record<string, number | null>;
    result: number | null;
}

const sortValue = (row: PoolRow, key: SortKey) =>
    key === 'result' ? row.result : key === 'map' ? null : row.ratings[key.slice('player:'.length)] ?? null;

/** Sorts rows for display. Unrated rows always go last, whichever way the column is sorted. */
export function sortRows(rows: PoolRow[], sort: SortState): PoolRow[] {
    const byMap = (a: PoolRow, b: PoolRow) => a.map.localeCompare(b.map, undefined, { sensitivity: 'base' });
    if (sort.key === 'map') return [...rows].sort((a, b) => (sort.desc ? -byMap(a, b) : byMap(a, b)));

    return [...rows].sort((a, b) => {
        const x = sortValue(a, sort.key);
        const y = sortValue(b, sort.key);
        if (x === null && y === null) return byMap(a, b);
        if (x === null) return 1;
        if (y === null) return -1;
        if (x !== y) return sort.desc ? y - x : x - y;
        return byMap(a, b);
    });
}

/** Clicking a column: a new numeric column starts with the best first; clicking it again flips it. */
export const nextSort = (current: SortState, key: SortKey): SortState =>
    current.key === key ? { key, desc: !current.desc } : { key, desc: key !== 'map' };

const SORT_KEY = 'rs-map-pool-sort';

/** Each side's sort is remembered per browser; it's never sent to the server. */
export function loadSorts(): Record<MapSide, SortState> {
    const fallback = { T: DEFAULT_SORT, CT: DEFAULT_SORT };
    try {
        const saved = JSON.parse(localStorage.getItem(SORT_KEY) ?? 'null') as Partial<Record<MapSide, SortState>> | null;
        const valid = (s: SortState | undefined): s is SortState =>
            !!s && typeof s.desc === 'boolean' && typeof s.key === 'string' && (s.key === 'map' || s.key === 'result' || s.key.startsWith('player:'));
        return { T: valid(saved?.T) ? saved.T : DEFAULT_SORT, CT: valid(saved?.CT) ? saved.CT : DEFAULT_SORT };
    } catch {
        return fallback;
    }
}

export function saveSorts(sorts: Record<MapSide, SortState>) {
    try {
        localStorage.setItem(SORT_KEY, JSON.stringify(sorts));
    } catch {
        // Storage can be blocked (private mode); the sort just won't be remembered.
    }
}
