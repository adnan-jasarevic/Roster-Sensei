import { useMemo, useState } from 'react';
import { ArrowDownRight, ArrowUpRight, ChevronDown, ChevronUp, ChevronsUpDown, FilterX, Star } from 'lucide-react';
import { type Resource } from '../hooks/use-resource';
import { cx } from '../lib/cx';
import { MAP_POOL } from '../constants/maps';
import {
    DEFAULT_PLAYER_SORT,
    FORM_MATCHES,
    filterRatedMatches,
    formatPlayerRating,
    nextPlayerSort,
    playerStats,
    sortPlayerStats,
    type MatchTypeFilter,
    type PeriodFilter,
    type PlayerStatsSort,
    type PlayerStatsSortKey,
} from '../lib/player-ratings';
import { type Match } from '../types';
import { Button, Card, EmptyState, ErrorState, Field, LoadingRegion, SegmentedControl, Select, Skeleton } from './ui';

interface PlayerStatisticsProps {
    matches: Resource<Match[]>;
    /** The match type picked at the top of the Statistics tab. */
    typeFilter: MatchTypeFilter;
}

const PERIODS: { value: PeriodFilter; label: string }[] = [
    { value: '30d', label: '30 days' },
    { value: '90d', label: '90 days' },
    { value: 'year', label: 'This year' },
    { value: 'all', label: 'All time' },
];

const MAP_OPTIONS = [{ value: 'all', label: 'All maps' }, ...MAP_POOL.map(map => ({ value: map, label: map }))];

const MIN_MATCHES_OPTIONS = [1, 3, 5, 10].map(value => ({ value, label: value === 1 ? 'Any number' : `At least ${value}` }));

const DEFAULT_PERIOD: PeriodFilter = 'all';
const DEFAULT_MAP = 'all';
const DEFAULT_MIN_MATCHES = 1;

const COLUMNS: { key: PlayerStatsSortKey; label: string; numeric: boolean; hint?: string }[] = [
    { key: 'username', label: 'Player', numeric: false },
    { key: 'matches', label: 'Matches', numeric: true, hint: 'Rated matches' },
    { key: 'average', label: 'Average', numeric: true },
    { key: 'form', label: 'Form', numeric: true, hint: `Average of the last ${FORM_MATCHES} rated matches` },
    { key: 'best', label: 'Best', numeric: true },
    { key: 'worst', label: 'Worst', numeric: true },
];

/** Match ratings per player, filtered by type, period, map and a minimum number of matches. */
export default function PlayerStatistics({ matches, typeFilter }: PlayerStatisticsProps) {
    const [period, setPeriod] = useState<PeriodFilter>(DEFAULT_PERIOD);
    const [map, setMap] = useState<string>(DEFAULT_MAP);
    const [minMatches, setMinMatches] = useState(DEFAULT_MIN_MATCHES);
    const [sort, setSort] = useState<PlayerStatsSort>(DEFAULT_PLAYER_SORT);
    // Fixed while the page is open, so "last 30 days" doesn't shift under the table.
    const [now] = useState(() => new Date());

    const filters = useMemo(() => ({ type: typeFilter, period, map, minMatches }), [typeFilter, period, map, minMatches]);
    const hasAnyRating = useMemo(
        () => (matches.data ?? []).some(m => m.lineup?.some(p => p.rating != null)),
        [matches.data],
    );
    const ratedMatchCount = useMemo(
        () => filterRatedMatches(matches.data ?? [], filters, now).filter(m => m.lineup?.some(p => p.played && p.rating != null)).length,
        [matches.data, filters, now],
    );
    const rows = useMemo(() => sortPlayerStats(playerStats(matches.data ?? [], filters, now), sort), [matches.data, filters, now, sort]);

    const filtersChanged = period !== DEFAULT_PERIOD || map !== DEFAULT_MAP || minMatches !== DEFAULT_MIN_MATCHES;
    const clearFilters = () => {
        setPeriod(DEFAULT_PERIOD);
        setMap(DEFAULT_MAP);
        setMinMatches(DEFAULT_MIN_MATCHES);
    };

    return (
        <section aria-labelledby="player-statistics-title" className="mb-10">
            <div className="mb-4 min-w-0">
                <h2 id="player-statistics-title" className="text-xl font-black tracking-tight text-fg">Player statistics</h2>
                <p className="text-sm text-fg-subtle">Match ratings per player. The match type switch at the top applies here too.</p>
            </div>

            <div className="mb-4 flex flex-wrap items-end gap-x-4 gap-y-3">
                <SegmentedControl label="Period" value={period} options={PERIODS} onChange={setPeriod} />
                <Field label="Map" className="w-40">
                    <Select value={map} options={MAP_OPTIONS} onChange={setMap} />
                </Field>
                <Field label="Rated matches" className="w-40">
                    <Select value={minMatches} options={MIN_MATCHES_OPTIONS} onChange={setMinMatches} />
                </Field>
                {filtersChanged && (
                    <Button variant="ghost" size="sm" onClick={clearFilters} leftIcon={<FilterX className="size-4" aria-hidden="true" />} className="mb-1">
                        Clear filters
                    </Button>
                )}
            </div>

            {matches.isLoading ? (
                <Card>
                    <LoadingRegion label="Loading player statistics" className="flex flex-col gap-3 p-4">
                        {Array.from({ length: 5 }, (_, index) => <Skeleton key={index} className="h-9 w-full" />)}
                    </LoadingRegion>
                </Card>
            ) : matches.error && !matches.data ? (
                <Card><ErrorState title="Couldn't load player statistics" message={matches.error} onRetry={matches.reload} retrying={matches.isFetching} /></Card>
            ) : !hasAnyRating ? (
                <Card>
                    <EmptyState
                        icon={<Star />}
                        title="No player ratings yet"
                        description="Add ratings when you add a finished match, or in a match's lineup."
                    />
                </Card>
            ) : rows.length === 0 ? (
                <Card>
                    <EmptyState
                        icon={<FilterX />}
                        title="No players match these filters"
                        description="Try a longer period, another map or a lower number of rated matches."
                        action={filtersChanged ? <Button variant="secondary" onClick={clearFilters}>Clear filters</Button> : undefined}
                    />
                </Card>
            ) : (
                <Card className="overflow-hidden">
                    <p className="border-b border-line px-4 py-2.5 text-xs text-fg-subtle" aria-live="polite">
                        {rows.length} {rows.length === 1 ? 'player' : 'players'} · {ratedMatchCount} rated {ratedMatchCount === 1 ? 'match' : 'matches'}
                    </p>
                    <div className="overflow-x-auto" tabIndex={0} role="region" aria-label="Player statistics table">
                        <table className="w-full min-w-[34rem] border-separate border-spacing-0 text-sm">
                            <caption className="sr-only">Match ratings per player. Column headers sort the table.</caption>
                            <thead className="bg-sunken">
                                <tr>
                                    {COLUMNS.map(column => (
                                        <SortHeader key={column.key} columnKey={column.key} label={column.label} numeric={column.numeric} hint={column.hint} sort={sort} onSort={key => setSort(current => nextPlayerSort(current, key))} />
                                    ))}
                                </tr>
                            </thead>
                            <tbody>
                                {rows.map(row => {
                                    const formDelta = row.form - row.average;
                                    return (
                                        <tr key={row.userId} className="hover:bg-hover">
                                            <th scope="row" className="border-b border-line px-4 py-2.5 text-left font-semibold text-fg">{row.username}</th>
                                            <td className="border-b border-line px-4 py-2.5 text-right text-fg-muted tabular-nums">{row.matches}</td>
                                            <td className="border-b border-line px-4 py-2.5 text-right font-bold text-fg tabular-nums">{formatPlayerRating(row.average)}</td>
                                            <td className="border-b border-line px-4 py-2.5 text-right tabular-nums">
                                                <span className="inline-flex items-center justify-end gap-1 text-fg">
                                                    {/* Only worth an arrow when the recent matches differ noticeably from the average. */}
                                                    {row.matches > FORM_MATCHES && Math.abs(formDelta) >= 0.05 && (
                                                        formDelta > 0
                                                            ? <><ArrowUpRight className="size-4 text-success" aria-hidden="true" /><span className="sr-only">up from the average, </span></>
                                                            : <><ArrowDownRight className="size-4 text-danger" aria-hidden="true" /><span className="sr-only">down from the average, </span></>
                                                    )}
                                                    {formatPlayerRating(row.form)}
                                                </span>
                                            </td>
                                            <td className="border-b border-line px-4 py-2.5 text-right text-fg-muted tabular-nums">{formatPlayerRating(row.best)}</td>
                                            <td className="border-b border-line px-4 py-2.5 text-right text-fg-muted tabular-nums">{formatPlayerRating(row.worst)}</td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                </Card>
            )}
        </section>
    );
}

interface SortHeaderProps {
    columnKey: PlayerStatsSortKey;
    label: string;
    numeric: boolean;
    hint?: string;
    sort: PlayerStatsSort;
    onSort: (key: PlayerStatsSortKey) => void;
}

function SortHeader({ columnKey, label, numeric, hint, sort, onSort }: SortHeaderProps) {
    const sorted = sort.key === columnKey ? (sort.desc ? 'desc' : 'asc') : null;
    const Icon = sorted === 'asc' ? ChevronUp : sorted === 'desc' ? ChevronDown : ChevronsUpDown;

    return (
        <th
            scope="col"
            aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined}
            className="h-11 border-b border-line px-4 font-semibold text-fg"
        >
            <button
                type="button"
                onClick={() => onSort(columnKey)}
                title={hint}
                className={cx('-mx-1 flex w-[calc(100%+0.5rem)] items-center gap-1 rounded px-1 py-1 hover:text-accent-text', numeric ? 'flex-row-reverse text-right' : 'text-left')}
            >
                <span className="truncate">{label}</span>
                <Icon className={cx('size-4 shrink-0', !sorted && 'text-fg-subtle')} aria-hidden="true" />
            </button>
        </th>
    );
}
