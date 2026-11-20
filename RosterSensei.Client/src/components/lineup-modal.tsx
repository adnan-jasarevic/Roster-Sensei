import { useCallback, useId, useState, type FormEvent, type KeyboardEvent } from 'react';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { formatPlayerRating, parsePlayerRating } from '../lib/player-ratings';
import { cx } from '../lib/cx';
import { EMPTY_STATS, parseStat, statsError, statsToRequest, statsToTyped, type StatKey, type TypedStats } from '../lib/match-stats';
import {
    hasResult,
    LINEUP_MAX_PLAYERS,
    LINEUP_MAX_ROLE_LENGTH,
    type LineupEntry,
    type Match,
    type SetLineupRequest,
    type TeamMember,
} from '../types';
import { Alert, Button, ErrorState, LoadingRegion, Modal, Skeleton } from './ui';

interface LineupModalProps {
    teamId: number;
    match: Match;
    onClose: () => void;
    /** Called with the lineup the API saved. */
    onSaved: (lineup: LineupEntry[]) => void;
    /** The match is gone (deleted elsewhere): the list should refresh. */
    onMatchMissing: () => void;
}

interface Pick {
    userId: string;
    role: string;
    played: boolean;
    /** As typed; empty means not rated. */
    rating: string;
    /** Kills, deaths, assists, ADR and HS% as typed; empty means not recorded. */
    stats: TypedStats;
}

/** The rating and stat columns of a finished match, in the order they're typed in. */
const GRID_COLUMNS: { key: 'rating' | StatKey; label: string; title: string; numeric: boolean }[] = [
    { key: 'rating', label: 'Rating', title: 'Rating', numeric: false },
    { key: 'kills', label: 'K', title: 'Kills', numeric: true },
    { key: 'deaths', label: 'D', title: 'Deaths', numeric: true },
    { key: 'assists', label: 'A', title: 'Assists', numeric: true },
    { key: 'adr', label: 'ADR', title: 'Average damage per round', numeric: false },
    { key: 'headshotPercentage', label: 'HS%', title: 'Headshot percentage', numeric: false },
];

const headCell = 'border-b border-r border-line px-3 py-2 font-semibold last:border-r-0';

// Borderless, so the table's grid lines are the only lines: it reads like a spreadsheet.
const cellInput = cx(
    'h-10 w-full bg-transparent px-3 text-base text-fg placeholder:text-fg-subtle sm:text-sm',
    'focus:bg-sunken focus:outline-none focus:ring-2 focus:ring-inset focus:ring-fg',
    'aria-invalid:bg-danger/10 aria-invalid:ring-2 aria-invalid:ring-inset aria-invalid:ring-danger',
);

const cellIsValid = (key: 'rating' | StatKey, text: string) =>
    key === 'rating' ? parsePlayerRating(text).ok : parseStat(key, text).ok;

/** Marks a cell with its row and column, so Enter and the arrow keys can move between rows. */
const cellNavigation = (row: number, column: string) => ({ 'data-row': row, 'data-column': column });

/** Enter or Arrow Down moves to the same column in the next row that has it, Arrow Up to the one above. */
function moveBetweenRows(event: KeyboardEvent<HTMLTableSectionElement>) {
    const input = event.target as HTMLElement;
    const column = input.dataset.column;
    if (!column || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const step = event.key === 'Enter' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0;
    if (step === 0) return;

    const cells = Array.from(event.currentTarget.querySelectorAll<HTMLInputElement>(`input[data-column="${column}"]`));
    const next = cells[cells.indexOf(input as HTMLInputElement) + step];
    if (!next) return; // Enter on the last row submits the form as usual.
    event.preventDefault();
    next.focus();
    next.select();
}

/** What's wrong with a typed rating, or undefined when it's fine (or empty). */
const ratingErrorOf = (text: string) => {
    const parsed = parsePlayerRating(text);
    return parsed.ok ? undefined : parsed.message;
};

export default function LineupModal({ teamId, match, onClose, onSaved, onMatchMissing }: LineupModalProps) {
    const loadMembers = useCallback<ResourceLoader<TeamMember[]>>(
        signal => api.get<TeamMember[]>(`/Teams/${teamId}/members`, { signal }).then(response => response.data),
        [teamId],
    );
    const { data: members, error: membersError, isLoading, isFetching, reload } = useResource(loadMembers, 'Failed to load the roster.');

    // In the order players were added, which the API keeps.
    const [picks, setPicks] = useState<Pick[]>(() =>
        (match.lineup ?? []).map(entry => ({
            userId: entry.userId,
            role: entry.role ?? '',
            played: entry.played,
            rating: entry.rating != null ? formatPlayerRating(entry.rating) : '',
            stats: statsToTyped(entry),
        })),
    );
    const save = useAction();
    const [showErrors, setShowErrors] = useState(false);
    // Only a finished match can be rated or get stats, and only players who played.
    const canRate = hasResult(match);
    const isSaving = save.isRunning;

    const memberIds = new Set(members?.map(m => m.id));
    // Players in the saved lineup who have since left the team. The API only accepts current
    // members, so they are dropped when saving.
    const formerPlayers = members ? (match.lineup ?? []).filter(entry => !memberIds.has(entry.userId)) : [];
    const currentPicks = members ? picks.filter(p => memberIds.has(p.userId)) : picks;
    const isFull = currentPicks.length >= LINEUP_MAX_PLAYERS;
    const starters = currentPicks.filter(p => p.played).length;

    const pickFor = (userId: string) => picks.find(p => p.userId === userId);

    const errorsId = useId();
    // Shown under the table once a save was tried, one line per player with a problem.
    const errors = showErrors && canRate && members
        ? members.flatMap(member => {
            const pick = pickFor(member.id);
            const message = pick?.played ? ratingErrorOf(pick.rating) ?? statsError(pick.stats) : undefined;
            return message ? [{ userId: member.id, username: member.username, message }] : [];
        })
        : [];

    const toggle = (userId: string) => {
        setPicks(current => current.some(p => p.userId === userId)
            ? current.filter(p => p.userId !== userId)
            : [...current, { userId, role: '', played: true, rating: '', stats: EMPTY_STATS }]);
    };

    const update = (userId: string, change: Partial<Pick>) => {
        setPicks(current => current.map(p => (p.userId === userId ? { ...p, ...change } : p)));
    };

    const handleSubmit = async (event: FormEvent) => {
        event.preventDefault();
        if (!members) return;
        if (canRate && currentPicks.some(p => p.played && (ratingErrorOf(p.rating) || statsError(p.stats)))) {
            save.reset();
            setShowErrors(true);
            return;
        }

        const body: SetLineupRequest = {
            players: currentPicks.map(p => {
                const rating = parsePlayerRating(p.rating);
                return {
                    userId: p.userId,
                    role: p.role.trim() || undefined,
                    played: p.played,
                    rating: canRate && p.played && rating.ok && rating.value !== null ? rating.value : undefined,
                    ...(canRate && p.played ? statsToRequest(p.stats) : {}),
                };
            }),
        };

        const result = await save.run(() => api.put<LineupEntry[]>(`/Matches/${teamId}/${match.id}/lineup`, body), 'Failed to save the lineup.');
        if (result.ok) {
            onSaved(result.data.data);
            return;
        }
        if (result.httpStatus === 404) onMatchMissing();
        // A player who left in the meantime makes the API refuse the lineup: show the current roster.
        if (result.httpStatus === 400) reload();
    };

    const formId = `lineup-form-${match.id}`;

    return (
        <Modal
            open
            onClose={onClose}
            title={`Lineup vs ${match.opponentName}`}
            description={`Tick up to ${LINEUP_MAX_PLAYERS} players. Untick "Played" for a substitute who didn't play. Every cell is optional${canRate ? '; stats are for the whole match, ratings from 0.01 to 99.99' : ''}.`}
            size={canRate ? 'xl' : 'md'}
            busy={isSaving}
            closeOnBackdrop={false}
            footer={
                <>
                    <Button variant="ghost" onClick={onClose} disabled={isSaving}>Cancel</Button>
                    <Button type="submit" form={formId} disabled={!members} loading={isSaving} loadingText="Saving...">
                        Save lineup
                    </Button>
                </>
            }
        >
            {isLoading ? (
                <LoadingRegion label="Loading the roster" className="space-y-3">
                    {Array.from({ length: 4 }, (_, index) => <Skeleton key={index} className="h-11 w-full" />)}
                </LoadingRegion>
            ) : membersError && !members ? (
                <ErrorState compact title="Couldn't load the roster" message={membersError} onRetry={reload} retrying={isFetching} />
            ) : (
                <form id={formId} onSubmit={handleSubmit} className="flex flex-col gap-4">
                    {save.error && <Alert tone="danger">{save.error}</Alert>}
                    {formerPlayers.length > 0 && (
                        <Alert tone="warning">
                            {formerPlayers.map(p => p.username).join(', ')} {formerPlayers.length === 1 ? 'is' : 'are'} no longer on the team and will be removed from this lineup when you save.
                        </Alert>
                    )}

                    <p className="text-sm text-fg-muted" aria-live="polite">
                        {currentPicks.length} of {LINEUP_MAX_PLAYERS} selected · {starters} played
                    </p>

                    <div className="overflow-x-auto rounded-md border border-line" role="region" aria-label="Lineup table" tabIndex={-1}>
                        <table className={cx('w-full border-separate border-spacing-0 text-sm', canRate ? 'min-w-[46rem]' : 'min-w-[26rem]')}>
                            <caption className="sr-only">
                                One row per team member. Tick a player to add them, then type in the cells. Enter moves down a column.
                            </caption>
                            <thead className="bg-sunken text-xs text-fg-muted">
                                <tr>
                                    <th scope="col" className={cx(headCell, 'sticky left-0 z-10 bg-sunken text-left')}>Player</th>
                                    <th scope="col" className={cx(headCell, 'text-left')}>Role</th>
                                    <th scope="col" className={cx(headCell, 'w-16 text-center')}>Played</th>
                                    {canRate && GRID_COLUMNS.map(column => (
                                        <th key={column.key} scope="col" className={cx(headCell, 'w-[4.5rem] text-right')}>
                                            <abbr title={column.title} className="no-underline">{column.label}</abbr>
                                        </th>
                                    ))}
                                </tr>
                            </thead>
                            <tbody onKeyDown={moveBetweenRows} className="[&>tr:last-child>*]:border-b-0">
                                {(members ?? []).map((member, rowIndex) => {
                                    const pick = pickFor(member.id);
                                    const canHaveStats = canRate && !!pick?.played;
                                    return (
                                        <tr key={member.id} className={cx(!pick && 'text-fg-subtle')}>
                                            <th scope="row" className="sticky left-0 z-10 border-b border-r border-line bg-raised p-0 text-left font-normal">
                                                <label className="flex h-10 cursor-pointer items-center gap-2.5 px-3 whitespace-nowrap">
                                                    <input
                                                        type="checkbox"
                                                        className="size-4 shrink-0 accent-(--rs-accent)"
                                                        checked={!!pick}
                                                        disabled={!pick && isFull}
                                                        onChange={() => toggle(member.id)}
                                                    />
                                                    <span className={cx('font-semibold', pick ? 'text-fg' : 'text-fg-muted')}>{member.username}</span>
                                                </label>
                                            </th>
                                            <td className="border-b border-r border-line p-0">
                                                {pick && (
                                                    <input
                                                        {...cellNavigation(rowIndex, 'role')}
                                                        aria-label={`Role of ${member.username}`}
                                                        value={pick.role}
                                                        onChange={e => update(member.id, { role: e.target.value })}
                                                        maxLength={LINEUP_MAX_ROLE_LENGTH}
                                                        placeholder="e.g. AWP"
                                                        autoComplete="off"
                                                        className={cx(cellInput, 'min-w-24 text-left')}
                                                    />
                                                )}
                                            </td>
                                            <td className="border-b border-r border-line p-0 text-center">
                                                {pick && (
                                                    <input
                                                        type="checkbox"
                                                        aria-label={`${member.username} played`}
                                                        className="size-4 accent-(--rs-accent) align-middle"
                                                        checked={pick.played}
                                                        // A substitute who didn't play can't keep a rating or stats.
                                                        onChange={e => update(member.id, e.target.checked ? { played: true } : { played: false, rating: '', stats: EMPTY_STATS })}
                                                    />
                                                )}
                                            </td>
                                            {canRate && GRID_COLUMNS.map(column => {
                                                const text = !pick ? '' : column.key === 'rating' ? pick.rating : pick.stats[column.key];
                                                const invalid = showErrors && canHaveStats && !cellIsValid(column.key, text);
                                                return (
                                                    <td key={column.key} className="border-b border-r border-line p-0 last:border-r-0">
                                                        {canHaveStats && (
                                                            <input
                                                                {...cellNavigation(rowIndex, column.key)}
                                                                aria-label={`${column.title} of ${member.username}`}
                                                                aria-invalid={invalid || undefined}
                                                                aria-describedby={invalid ? errorsId : undefined}
                                                                value={text}
                                                                onChange={e => update(member.id, column.key === 'rating'
                                                                    ? { rating: e.target.value }
                                                                    : { stats: { ...pick!.stats, [column.key]: e.target.value } })}
                                                                inputMode={column.numeric ? 'numeric' : 'decimal'}
                                                                autoComplete="off"
                                                                className={cx(cellInput, 'w-full text-right tabular-nums')}
                                                            />
                                                        )}
                                                    </td>
                                                );
                                            })}
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                    {errors.length > 0 && (
                        <ul id={errorsId} className="flex flex-col gap-0.5 text-xs font-medium text-danger">
                            {errors.map(error => <li key={error.userId}><span className="font-bold">{error.username}</span> {error.message}</li>)}
                        </ul>
                    )}
                    {isFull && <p className="text-xs text-fg-subtle">A lineup holds at most {LINEUP_MAX_PLAYERS} players. Untick someone to add another.</p>}
                </form>
            )}
        </Modal>
    );
}
