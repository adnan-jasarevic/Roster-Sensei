import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { ChevronDown, ChevronUp, ChevronsUpDown, RefreshCw } from 'lucide-react';
import api from '../services/api';
import { getApiErrorMessage } from '../services/errors';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { cx } from '../lib/cx';
import {
    DEFAULT_SORT, SIDES, average, cellKey, formatRating, loadSorts, nextSort, parseRating, ratingsByCell, saveSorts, sortRows,
    type PoolRow, type SortKey, type SortState,
} from '../lib/map-pool';
import { type MapPool, type MapSide, type SaveMapRatingRequest, type Team } from '../types';
import PageHeader, { PageContainer } from '../components/page-header';
import { PanelSkeleton } from '../components/page-skeletons';
import TeamGate from '../components/team-gate';
import { Alert, Badge, Button, Card, ErrorState, LoadingRegion, useToast } from '../components/ui';

const SIDE_LABELS: Record<MapSide, string> = { T: 'T side', CT: 'CT side' };

export default function MapPoolPage() {
    return (
        <TeamGate title="Map Pool">
            {team => <MapPoolContent key={team.id} team={team} />}
        </TeamGate>
    );
}

function MapPoolContent({ team }: { team: Team }) {
    const load = useCallback<ResourceLoader<MapPool>>(
        signal => api.get<MapPool>(`/teams/${team.id}/map-pool`, { signal }).then(response => response.data),
        [team.id],
    );
    const { data: pool, error, isLoading, isFetching, reload } = useResource(load, 'Failed to load the map pool.', { keepPrevious: true });
    const toast = useToast();

    // The signed-in player's own ratings as they last typed them, over what the server sent. Only they
    // can change their column, so these stay right across refreshes; a failed save puts the old value back.
    const [ownRatings, setOwnRatings] = useState<Record<string, number | null>>({});
    const [savingCount, setSavingCount] = useState(0);
    // The last value the server accepted per cell, to go back to when a later save of that cell fails.
    const confirmed = useRef(new Map<string, number | null>());
    // Saves of one cell run one after another, so a quick 7 then 8 can't reach the server as 8 then 7.
    const queues = useRef(new Map<string, Promise<void>>());
    const latest = useRef(new Map<string, number>());
    const saveCounter = useRef(0);

    const [sorts, setSorts] = useState(loadSorts);
    useEffect(() => saveSorts(sorts), [sorts]);

    const serverRatings = useMemo(() => ratingsByCell(pool?.ratings ?? []), [pool]);
    const me = pool?.currentUserId;

    const saveRating = useCallback((side: MapSide, mapName: string, value: number | null) => {
        if (!me) return;
        const key = cellKey(side, mapName, me);
        const id = ++saveCounter.current;
        latest.current.set(key, id);
        setOwnRatings(current => ({ ...current, [key]: value }));
        setSavingCount(count => count + 1);

        const path = `/teams/${team.id}/map-pool/${side}/${encodeURIComponent(mapName)}`;
        const previous = queues.current.get(key) ?? Promise.resolve();
        const run = previous
            .then(() => (value === null ? api.delete(path) : api.put(path, { rating: value } satisfies SaveMapRatingRequest)))
            .then(
                () => {
                    confirmed.current.set(key, value);
                },
                (err: unknown) => {
                    toast.error(getApiErrorMessage(err, `Couldn't save your ${SIDE_LABELS[side]} rating for ${mapName}.`));
                    // A newer save of this cell is on its way and will set the value itself.
                    if (latest.current.get(key) !== id) return;
                    setOwnRatings(current => {
                        const next = { ...current };
                        if (confirmed.current.has(key)) next[key] = confirmed.current.get(key)!;
                        else delete next[key];
                        return next;
                    });
                },
            )
            .finally(() => {
                setSavingCount(count => count - 1);
                if (queues.current.get(key) === run) queues.current.delete(key);
            });
        queues.current.set(key, run);
    }, [me, team.id, toast]);

    const ratingOf = useCallback((side: MapSide, mapName: string, userId: string) => {
        const key = cellKey(side, mapName, userId);
        if (userId === me && key in ownRatings) return ownRatings[key];
        return serverRatings.get(key) ?? null;
    }, [me, ownRatings, serverRatings]);

    return (
        <PageContainer>
            <PageHeader
                title="Map Pool"
                description="How strong is the team on each side of every map? Everyone rates from 1 to 10 in their own column; higher is better."
                actions={pool && (
                    <Button variant="secondary" onClick={reload} loading={isFetching} loadingText="Refreshing..." leftIcon={<RefreshCw className="size-4" aria-hidden="true" />}>
                        Refresh
                    </Button>
                )}
            />

            {error && pool && <Alert tone="danger" className="mb-4">{error}</Alert>}

            {isLoading ? (
                <LoadingRegion label="Loading the map pool" className="flex flex-col gap-6">
                    <PanelSkeleton height="h-80" />
                    <PanelSkeleton height="h-80" />
                </LoadingRegion>
            ) : error && !pool ? (
                <Card>
                    <ErrorState title="Couldn't load the map pool" message={error} onRetry={reload} retrying={isFetching} />
                </Card>
            ) : pool && (
                <>
                    <p className="mb-4 text-sm text-fg-muted" aria-live="polite">
                        {savingCount > 0 ? 'Saving...' : 'Type a rating like 7 or 6.5 and press Enter. Empty a cell to clear it. Click a column header to sort.'}
                    </p>
                    <div className="flex flex-col gap-6">
                        {SIDES.map(side => (
                            <SideTable
                                key={side}
                                side={side}
                                pool={pool}
                                ratingOf={ratingOf}
                                sort={sorts[side]}
                                onSort={key => setSorts(current => ({ ...current, [side]: nextSort(current[side], key) }))}
                                onSave={saveRating}
                            />
                        ))}
                    </div>
                </>
            )}
        </PageContainer>
    );
}

interface SideTableProps {
    side: MapSide;
    pool: MapPool;
    ratingOf: (side: MapSide, mapName: string, userId: string) => number | null;
    sort: SortState;
    onSort: (key: SortKey) => void;
    onSave: (side: MapSide, mapName: string, value: number | null) => void;
}

function SideTable({ side, pool, ratingOf, sort, onSort, onSave }: SideTableProps) {
    const tableRef = useRef<HTMLTableElement>(null);
    const { players, currentUserId } = pool;

    // A saved sort on a player who has since left falls back to map order.
    const activeSort = sort.key.startsWith('player:') && !players.some(p => `player:${p.userId}` === sort.key) ? DEFAULT_SORT : sort;

    const rows = useMemo(() => {
        const built: PoolRow[] = pool.maps.map(map => {
            const ratings = Object.fromEntries(players.map(p => [p.userId, ratingOf(side, map, p.userId)]));
            return { map, ratings, result: average(Object.values(ratings)) };
        });
        return sortRows(built, activeSort);
    }, [pool.maps, players, ratingOf, side, activeSort]);

    // Enter moves down the column like a spreadsheet; the last row wraps to the first.
    const focusNextRow = (map: string) => {
        const inputs = Array.from(tableRef.current?.querySelectorAll<HTMLInputElement>('input[data-map]') ?? []);
        const index = inputs.findIndex(input => input.dataset.map === map);
        inputs[(index + 1) % inputs.length]?.focus();
    };

    const headingId = `map-pool-${side}`;

    return (
        <Card>
            <header className="flex items-center gap-2 px-5 pt-5 pb-4">
                <Badge tone={side === 'T' ? 'warning' : 'neutral'}>{side}</Badge>
                <h2 id={headingId} className="text-base font-bold text-fg">{SIDE_LABELS[side]}</h2>
            </header>
            {/* Scrollable regions need to be reachable by keyboard. Relative, so screen-reader-only text
                inside is clipped here instead of widening the page. */}
            <div className="relative overflow-x-auto border-t border-line" tabIndex={0} role="region" aria-labelledby={headingId}>
                <table ref={tableRef} className="w-full min-w-max border-separate border-spacing-0 text-sm">
                    <caption className="sr-only">
                        {SIDE_LABELS[side]} ratings from 1 to 10, one row per map and one column per player. Result is the average; higher is better.
                    </caption>
                    <thead className="bg-sunken">
                        <tr>
                            <SortHeader label="Map" sortKey="map" sort={activeSort} onSort={onSort} sticky />
                            {players.map(player => (
                                <SortHeader
                                    key={player.userId}
                                    label={player.username}
                                    note={player.userId === currentUserId ? '(you)' : undefined}
                                    sortKey={`player:${player.userId}`}
                                    sort={activeSort}
                                    onSort={onSort}
                                    numeric
                                    highlight={player.userId === currentUserId}
                                />
                            ))}
                            <SortHeader label="Result" sortKey="result" sort={activeSort} onSort={onSort} numeric />
                        </tr>
                    </thead>
                    <tbody>
                        {rows.map(row => (
                            <tr key={row.map} className="hover:bg-hover">
                                <th scope="row" className="sticky left-0 z-[1] border-b border-line bg-surface px-4 py-2 text-left font-semibold whitespace-nowrap text-fg">{row.map}</th>
                                {players.map(player => {
                                    const value = row.ratings[player.userId];
                                    return player.userId === currentUserId ? (
                                        <td key={player.userId} className="border-b border-line bg-accent-soft px-2 py-1.5 text-center">
                                            <OwnRatingInput
                                                map={row.map}
                                                side={side}
                                                value={value}
                                                onSave={next => onSave(side, row.map, next)}
                                                onEnter={() => focusNextRow(row.map)}
                                            />
                                        </td>
                                    ) : (
                                        <td key={player.userId} className="border-b border-line px-4 py-2 text-center text-fg tabular-nums">
                                            {value == null ? <NotRated /> : formatRating(value)}
                                        </td>
                                    );
                                })}
                                <td className="border-b border-line px-4 py-2">
                                    <ResultCell value={row.result} />
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </Card>
    );
}

interface SortHeaderProps {
    label: string;
    note?: string;
    sortKey: SortKey;
    sort: SortState;
    onSort: (key: SortKey) => void;
    numeric?: boolean;
    highlight?: boolean;
    /** Stays in view while the table scrolls sideways, so phones always see which map a row is. */
    sticky?: boolean;
}

function SortHeader({ label, note, sortKey, sort, onSort, numeric = false, highlight = false, sticky = false }: SortHeaderProps) {
    const active = sort.key === sortKey;
    return (
        <th
            scope="col"
            aria-sort={active ? (sort.desc ? 'descending' : 'ascending') : undefined}
            className={cx(
                'h-11 border-b border-line px-2 font-semibold text-fg',
                highlight && 'bg-accent-soft',
                sticky && 'sticky left-0 z-[1] bg-sunken',
                numeric ? 'text-center' : 'text-left',
            )}
        >
            <button
                type="button"
                onClick={() => onSort(sortKey)}
                className={cx('inline-flex items-center gap-1 rounded px-2 py-1 whitespace-nowrap hover:text-accent-text', active && 'text-accent-text')}
            >
                <span>{label}</span>
                {note && <span className="font-normal text-fg-muted">{note}</span>}
                {active ? (sort.desc ? <ChevronDown className="size-4" aria-hidden="true" /> : <ChevronUp className="size-4" aria-hidden="true" />)
                    : <ChevronsUpDown className="size-3.5 text-fg-subtle" aria-hidden="true" />}
            </button>
        </th>
    );
}

interface OwnRatingInputProps {
    map: string;
    side: MapSide;
    value: number | null;
    onSave: (value: number | null) => void;
    onEnter: () => void;
}

/** The signed-in player's cell: type 1 to 10 with one decimal, saved on Enter or when leaving the cell. */
function OwnRatingInput({ map, side, value, onSave, onEnter }: OwnRatingInputProps) {
    // What's typed while editing, or an invalid entry kept so it can be fixed. Null shows the saved value.
    const [draft, setDraftState] = useState<string | null>(null);
    const [problem, setProblem] = useState<string | null>(null);
    const errorId = `rating-error-${side}-${map}`;

    // Enter commits and then moves focus, which blurs this cell and commits again before React has
    // re-rendered. The ref sees the first commit at once, so the rating is only saved once.
    const draftRef = useRef<string | null>(null);
    const setDraft = (next: string | null) => {
        draftRef.current = next;
        setDraftState(next);
    };

    const commit = () => {
        const text = draftRef.current;
        if (text === null) return true;
        const parsed = parseRating(text);
        if (!parsed.ok) {
            setProblem(parsed.message);
            return false;
        }
        setProblem(null);
        setDraft(null);
        if (parsed.value !== value) onSave(parsed.value);
        return true;
    };

    const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
        if (event.key === 'Enter') {
            event.preventDefault();
            if (commit()) onEnter();
        } else if (event.key === 'Escape') {
            setDraft(null);
            setProblem(null);
        }
    };

    return (
        <div className="flex flex-col items-center gap-1">
            <input
                type="text"
                inputMode="decimal"
                autoComplete="off"
                data-map={map}
                aria-label={`Your ${SIDE_LABELS[side]} rating for ${map}`}
                aria-invalid={problem ? true : undefined}
                aria-describedby={problem ? errorId : undefined}
                placeholder="–"
                maxLength={4}
                value={draft ?? (value == null ? '' : formatRating(value))}
                onFocus={event => event.target.select()}
                onChange={event => {
                    setDraft(event.target.value);
                    setProblem(null);
                }}
                onBlur={commit}
                onKeyDown={onKeyDown}
                className={cx(
                    'h-9 w-16 rounded-md border border-line-strong bg-surface text-center text-base text-fg tabular-nums sm:text-sm',
                    'placeholder:text-fg-subtle hover:border-fg-subtle focus:border-fg focus:ring-1 focus:ring-fg focus-visible:outline-none',
                    'aria-invalid:border-danger',
                )}
            />
            {problem && <p id={errorId} role="alert" className="max-w-32 text-xs font-medium whitespace-normal text-danger">{problem}</p>}
        </div>
    );
}

function NotRated() {
    return (
        <>
            <span aria-hidden="true" className="text-fg-subtle">–</span>
            <span className="sr-only">Not rated</span>
        </>
    );
}

/** The row's average, two decimals like the team's old sheet, with a bar to compare rows at a glance. */
function ResultCell({ value }: { value: number | null }) {
    if (value === null) {
        return (
            <span className="block text-center">
                <NotRated />
            </span>
        );
    }
    return (
        <span className="flex items-center justify-center gap-2">
            <span aria-hidden="true" className="h-1.5 w-12 overflow-hidden rounded-full bg-neutral-soft">
                <span className="block h-full rounded-full bg-accent" style={{ width: `${value * 10}%` }} />
            </span>
            <span className="w-10 text-right font-bold text-fg tabular-nums">{value.toFixed(2)}</span>
        </span>
    );
}
