import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import axios from 'axios';
import { addDays, format, isSameDay, startOfWeek } from 'date-fns';
import { CalendarDays, Check, ChevronLeft, ChevronRight, RotateCcw, UsersRound } from 'lucide-react';
import api from '../services/api';
import { getApiErrorMessage } from '../services/errors';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { cx } from '../lib/cx';
import {
    EMPTY_DAY, browserTimeZone, freeBySlot, hasAnyFreeTime, isFree, lineupMemberIds, lineupWindows, listNames, paintDay,
    slotTime, slotsOn, todayIn, weekDates, windowTimes,
} from '../lib/availability';
import { type MyAvailability, type Team, type TeamAvailability } from '../types';
import PageHeader, { PageContainer } from '../components/page-header';
import TeamGate from '../components/team-gate';
import SlotGrid, { type SlotCell, type SlotGridColumn } from '../components/slot-grid';
import { Alert, Badge, Button, Card, ErrorState, IconButton, LinkButton, LoadingRegion, SegmentedControl, Skeleton } from '../components/ui';

const PAGE_TITLE = 'Availability (When2Play)';

type View = 'team' | 'mine';
type MineMode = 'usual' | 'week';

const thisWeek = () => startOfWeek(new Date(), { weekStartsOn: 1 });

export default function AvailabilityPage() {
    return (
        <TeamGate title={PAGE_TITLE}>
            {team => <AvailabilityContent key={team.id} team={team} />}
        </TeamGate>
    );
}

function AvailabilityContent({ team }: { team: Team }) {
    const loadMine = useCallback<ResourceLoader<MyAvailability>>(
        signal => api.get<MyAvailability>('/availability', { signal }).then(response => response.data),
        [],
    );
    const mine = useResource(loadMine, 'Failed to load your availability.');
    // What the editor last showed, so switching views doesn't bring back the first load.
    const [edited, setEdited] = useState<MyAvailability | null>(null);
    const myAvailability = edited ?? mine.data;

    // Until the person picks, someone who hasn't filled anything in starts on their own grid.
    const [chosenView, setChosenView] = useState<View | null>(null);
    const view: View = chosenView ?? (mine.data && !hasAnyFreeTime(mine.data) ? 'mine' : 'team');

    // Shared by both views. The team view only mounts while shown, so it always loads what was just saved.
    const [weekStart, setWeekStart] = useState(thisWeek);

    return (
        <PageContainer>
            <PageHeader
                title={PAGE_TITLE}
                description="Paint when you can play. Whenever 5 players are free at once, the calendar marks it."
                actions={<LinkButton to="/calendar" leftIcon={<CalendarDays className="size-4" aria-hidden="true" />}>Open calendar</LinkButton>}
            />

            {mine.isLoading ? (
                <LoadingRegion label="Loading availability">
                    <Skeleton className="mb-4 h-11 w-64" />
                    <Skeleton className="h-[min(62dvh,40rem)] w-full rounded-lg" />
                </LoadingRegion>
            ) : (
                <>
                    <SegmentedControl
                        label="Show"
                        hideLabel
                        className="mb-4"
                        value={view}
                        onChange={setChosenView}
                        options={[
                            { value: 'team', label: 'Team' },
                            { value: 'mine', label: 'My availability' },
                        ]}
                    />

                    {view === 'team' ? (
                        <TeamView
                            team={team}
                            weekStart={weekStart}
                            onWeekChange={setWeekStart}
                            onFillIn={() => setChosenView('mine')}
                            filledIn={!myAvailability || hasAnyFreeTime(myAvailability)}
                        />
                    ) : myAvailability ? (
                        <MyAvailabilityEditor
                            initial={myAvailability}
                            onChange={setEdited}
                            weekStart={weekStart}
                            onWeekChange={setWeekStart}
                        />
                    ) : (
                        <Card>
                            <ErrorState title="Couldn't load your availability" message={mine.error} onRetry={mine.reload} retrying={mine.isFetching} />
                        </Card>
                    )}
                </>
            )}
        </PageContainer>
    );
}

function WeekNavigator({ weekStart, onChange }: { weekStart: Date; onChange: (week: Date) => void }) {
    const isThisWeek = isSameDay(weekStart, thisWeek());
    return (
        <div className="flex flex-wrap items-center gap-2">
            <div className="flex items-center gap-1">
                <IconButton label="Previous week" icon={<ChevronLeft className="size-5" aria-hidden="true" />} onClick={() => onChange(addDays(weekStart, -7))} />
                <IconButton label="Next week" icon={<ChevronRight className="size-5" aria-hidden="true" />} onClick={() => onChange(addDays(weekStart, 7))} />
            </div>
            <h2 className="font-display text-lg font-black text-fg tabular-nums" aria-live="polite">
                {format(weekStart, 'd MMM')} – {format(addDays(weekStart, 6), 'd MMM yyyy')}
            </h2>
            {!isThisWeek && <Button variant="ghost" size="sm" onClick={() => onChange(thisWeek())}>This week</Button>}
        </div>
    );
}

/** The day header of a grid column: short day name over the date, today in the accent colour. */
function DayHeader({ date, dated, children }: { date: Date; dated: boolean; children?: ReactNode }) {
    const today = dated && isSameDay(date, new Date());
    return (
        <>
            <span className="text-[0.6875rem] font-semibold tracking-wide text-fg-muted uppercase">{format(date, 'EEE')}</span>
            {dated && <span className={cx('text-sm font-bold tabular-nums', today ? 'text-accent-text' : 'text-fg')}>{format(date, 'd')}</span>}
            {children}
        </>
    );
}

// ---------------------------------------------------------------------------------------------
// My availability
// ---------------------------------------------------------------------------------------------

interface MyAvailabilityEditorProps {
    initial: MyAvailability;
    /** Every change, as soon as it shows (before it has been saved). */
    onChange: (availability: MyAvailability) => void;
    weekStart: Date;
    onWeekChange: (week: Date) => void;
}

// Any Monday serves as the template week's dates: only the weekday is used.
const TEMPLATE_MONDAY = new Date(2024, 0, 1);

function MyAvailabilityEditor({ initial, onChange, weekStart, onWeekChange }: MyAvailabilityEditorProps) {
    const [availability, setAvailabilityState] = useState(initial);
    // The latest value, for the next change made before React has rendered the last one.
    const current = useRef(initial);
    const setAvailability = (next: MyAvailability) => {
        current.current = next;
        setAvailabilityState(next);
        onChange(next);
    };

    const [mode, setMode] = useState<MineMode>('usual');
    const deviceZone = browserTimeZone();
    const timeZone = availability.timeZone ?? deviceZone;

    // Saves run one after another, so the server ends up with the last change, as the grid shows.
    const queue = useRef<Promise<void>>(Promise.resolve());
    const pending = useRef(0);
    const failure = useRef<string | null>(null);
    const [saveState, setSaveState] = useState<{ status: 'idle' | 'saving' | 'saved' } | { status: 'failed'; error: string }>({ status: 'idle' });

    const save = (send: () => Promise<unknown>) => {
        pending.current += 1;
        setSaveState({ status: 'saving' });
        queue.current = queue.current
            .then(send)
            .catch((error: unknown) => {
                failure.current ??= getApiErrorMessage(error, "Couldn't save your availability.");
            })
            .then(async () => {
                pending.current -= 1;
                if (pending.current > 0) return;
                const failed = failure.current;
                failure.current = null;
                if (!failed) {
                    setSaveState({ status: 'saved' });
                    return;
                }
                // Back to what the server has, so the grid never shows times that weren't saved.
                setSaveState({ status: 'failed', error: failed });
                try {
                    const { data } = await api.get<MyAvailability>('/availability');
                    setAvailability(data);
                } catch {
                    // Keep what is on screen; the error already says the save didn't happen.
                }
            });
    };

    const dates = useMemo(() => weekDates(mode === 'usual' ? TEMPLATE_MONDAY : weekStart), [mode, weekStart]);
    const today = todayIn(timeZone);
    const overridden = new Set(availability.overrides.map(o => o.date));

    const daySlots = (column: number) => (mode === 'usual' ? availability.weekly[column] ?? EMPTY_DAY : slotsOn(availability, dates[column]));

    const columns: SlotGridColumn[] = dates.map(date => {
        const day = parseDate(date);
        const changed = mode === 'week' && overridden.has(date);
        const past = mode === 'week' && date < today;
        return {
            key: `${mode}-${date}`,
            name: mode === 'usual' ? `Every ${format(day, 'EEEE')}` : format(day, 'EEEE d MMMM'),
            disabled: past,
            header: (
                <DayHeader date={day} dated={mode === 'week'}>
                    {changed && !past && (
                        <button
                            type="button"
                            className="mt-0.5 inline-flex h-6 items-center gap-1 rounded px-1.5 text-[0.6875rem] font-bold text-warning hover:bg-warning-soft focus-visible:outline-2 focus-visible:outline-(--rs-focus)"
                            aria-label={`${format(day, 'EEEE d MMMM')} differs from your usual week. Reset it to your usual ${format(day, 'EEEE')}`}
                            title="Differs from your usual week. Click to reset."
                            onClick={() => resetDay(date)}
                        >
                            <RotateCcw className="size-3" aria-hidden="true" />
                            <span className="max-sm:sr-only">Reset</span>
                        </button>
                    )}
                </DayHeader>
            ),
        };
    });

    const paint = (cells: SlotCell[], free: boolean) => {
        const before = current.current;
        const byColumn = new Map<number, number[]>();
        for (const cell of cells) byColumn.set(cell.column, [...(byColumn.get(cell.column) ?? []), cell.slot]);

        if (mode === 'usual') {
            const weekly = [...before.weekly];
            for (const [column, slots] of byColumn) weekly[column] = slots.reduce((day, slot) => paintDay(day, slot, slot, free), weekly[column] ?? EMPTY_DAY);
            setAvailability({ ...before, timeZone, weekly });
            save(() => api.put('/availability/weekly', { timeZone, weekly }));
            return;
        }

        let overrides = before.overrides;
        for (const [column, slots] of byColumn) {
            const date = dates[column];
            const day = slots.reduce((value, slot) => paintDay(value, slot, slot, free), slotsOn(before, date));
            overrides = [...overrides.filter(o => o.date !== date), { date, slots: day }].sort((a, b) => a.date.localeCompare(b.date));
            save(() => api.put(`/availability/days/${date}`, { timeZone, slots: day }));
        }
        setAvailability({ ...before, timeZone, overrides });
    };

    function resetDay(date: string) {
        const before = current.current;
        setAvailability({ ...before, overrides: before.overrides.filter(o => o.date !== date) });
        save(() => api.delete(`/availability/days/${date}`).catch((error: unknown) => {
            // Already back to the usual week (another tab got there first): that is what was asked for.
            if (axios.isAxiosError(error) && error.response?.status === 404) return;
            throw error;
        }));
    }

    const switchZone = () => {
        const before = current.current;
        setAvailability({ ...before, timeZone: deviceZone });
        save(() => api.put('/availability/weekly', { timeZone: deviceZone, weekly: before.weekly }));
    };

    // Scrolls to the first free half hour of the week, or to the evening when there's none.
    const [firstFree] = useState(() => {
        const slots = initial.weekly.map(day => day.indexOf('1')).filter(i => i >= 0);
        return slots.length ? Math.min(...slots) : 36;
    });

    return (
        <div className="flex flex-col gap-4">
            {availability.timeZone && availability.timeZone !== deviceZone && (
                <Alert tone="warning">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                        <p>Your times are in <strong>{availability.timeZone}</strong>, but this device is set to <strong>{deviceZone}</strong>. Teammates always see them at the right moment in their own time.</p>
                        <Button size="sm" variant="secondary" onClick={switchZone}>Use {deviceZone}</Button>
                    </div>
                </Alert>
            )}

            <div className="flex flex-wrap items-end justify-between gap-3">
                <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
                    <SegmentedControl
                        label="Edit"
                        hideLabel
                        value={mode}
                        onChange={setMode}
                        options={[
                            { value: 'usual', label: 'Usual week' },
                            { value: 'week', label: 'Specific week' },
                        ]}
                    />
                    {mode === 'week' && <WeekNavigator weekStart={weekStart} onChange={onWeekChange} />}
                </div>
                <SaveStatus state={saveState} />
            </div>

            {saveState.status === 'failed' && (
                <Alert tone="danger" onDismiss={() => setSaveState({ status: 'idle' })}>
                    {saveState.error} The grid shows what is saved.
                </Alert>
            )}

            <p className="max-w-[70ch] text-sm text-fg-muted">
                {mode === 'usual'
                    ? 'Drag across the times you can usually play. Start on a green time to clear instead. This repeats every week.'
                    : 'Changes here apply to these dates only, on top of your usual week. A changed day gets a Reset button.'}
                <span className="max-sm:hidden"> With a keyboard, the arrow keys move and Space switches a half hour.</span>
                <span className="sm:hidden"> Scroll by dragging the times on the left.</span>
            </p>

            <SlotGrid
                key={mode}
                label={mode === 'usual' ? 'Your usual week' : `Your availability, week of ${format(weekStart, 'd MMMM')}`}
                columns={columns}
                scrollToSlot={firstFree}
                paint={{
                    isFree: ({ column, slot }) => isFree(daySlots(column), slot),
                    onPaint: paint,
                }}
                renderCell={({ column, slot }, free) => ({
                    className: cx(free && 'is-free', free && slot > 0 && isFree(daySlots(column), slot - 1) && 'joins-above'),
                    description: free ? 'free' : 'not free',
                })}
            />

            <p className="text-xs text-fg-subtle">Times are in {timeZone}.</p>
        </div>
    );
}

function SaveStatus({ state }: { state: { status: string } }) {
    return (
        <p role="status" className="flex h-9 items-center gap-1.5 text-sm text-fg-muted">
            {state.status === 'saving' && 'Saving...'}
            {state.status === 'saved' && (
                <>
                    <Check className="size-4 text-success" aria-hidden="true" /> Saved
                </>
            )}
        </p>
    );
}

const parseDate = (date: string) => {
    const [y, m, d] = date.split('-').map(Number);
    return new Date(y, m - 1, d);
};

// ---------------------------------------------------------------------------------------------
// The team
// ---------------------------------------------------------------------------------------------

interface TeamViewProps {
    team: Team;
    weekStart: Date;
    onWeekChange: (week: Date) => void;
    onFillIn: () => void;
    filledIn: boolean;
}

function TeamView({ team, weekStart, onWeekChange, onFillIn, filledIn }: TeamViewProps) {
    const load = useCallback<ResourceLoader<TeamAvailability>>(signal => {
        const params = { from: weekStart.toISOString(), to: addDays(weekStart, 7).toISOString() };
        return api.get<TeamAvailability>(`/teams/${team.id}/availability`, { signal, params }).then(response => response.data);
    }, [team.id, weekStart]);
    const { data, error, isLoading, isFetching, reload } = useResource(load, "Failed to load the team's availability.", { keepPrevious: true });

    const [active, setActive] = useState<SlotCell | null>(null);

    const days = useMemo(() => Array.from({ length: 7 }, (_, i) => addDays(weekStart, i)), [weekStart]);
    const slotStart = useCallback((column: number, slot: number) => {
        const day = days[column];
        return new Date(day.getFullYear(), day.getMonth(), day.getDate(), 0, slot * 30);
    }, [days]);

    const model = useMemo(() => {
        if (!data) return null;
        const free = freeBySlot(data);
        const counting = lineupMemberIds(data);
        const freeAt = (column: number, slot: number) => free.get(slotStart(column, slot).getTime()) ?? [];
        const playersAt = (column: number, slot: number) => freeAt(column, slot).filter(id => counting.has(id)).length;
        return { free, counting, freeAt, playersAt, windows: lineupWindows(data) };
    }, [data, slotStart]);

    if (isLoading) {
        return (
            <LoadingRegion label="Loading the team's availability">
                <Skeleton className="mb-4 h-10 w-72" />
                <Skeleton className="h-[min(62dvh,40rem)] w-full rounded-lg" />
            </LoadingRegion>
        );
    }
    if (!data || !model) {
        return (
            <Card>
                <ErrorState title="Couldn't load the team's availability" message={error} onRetry={reload} retrying={isFetching} />
            </Card>
        );
    }

    const needed = data.playersNeeded;
    const players = data.members.filter(m => m.countsTowardsLineup);
    const levels = Array.from({ length: needed - 1 }, (_, i) => i + 1);
    const heatClass = (count: number) => {
        if (count <= 0) return undefined;
        if (count >= needed) return 'is-lineup';
        return `heat-${Math.max(1, Math.min(4, Math.ceil((count * 4) / (needed - 1))))}`;
    };

    const columns: SlotGridColumn[] = days.map(day => ({
        key: day.toISOString(),
        name: format(day, 'EEEE d MMMM'),
        header: <DayHeader date={day} dated />,
    }));

    return (
        <div className="flex flex-col gap-4">
            {error && <Alert tone="danger">{error}</Alert>}

            {players.length < needed && (
                <Alert tone="info">
                    A lineup needs {needed} players and {team.name} has {players.length}. Coaches don't count, so no time can be marked on the calendar yet.
                </Alert>
            )}

            {!filledIn && (
                <Alert tone="info">
                    <div className="flex flex-wrap items-center justify-between gap-3">
                        <p>You haven't said when you can play yet.</p>
                        <Button size="sm" variant="secondary" onClick={onFillIn}>Fill in my availability</Button>
                    </div>
                </Alert>
            )}

            <div className="flex flex-wrap items-center justify-between gap-3">
                <WeekNavigator weekStart={weekStart} onChange={onWeekChange} />
                <ul className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs font-medium text-fg-muted" aria-label="Players free">
                    <li className="mr-1">Players free</li>
                    {levels.map(level => (
                        <li key={level} className="flex items-center gap-1">
                            <span className={cx('rs-heat-swatch', heatClass(level))} aria-hidden="true" />
                            {level}
                        </li>
                    ))}
                    <li className="flex items-center gap-1 font-bold text-fg">
                        <span className="rs-heat-swatch is-lineup" aria-hidden="true" />
                        {needed}+ lineup
                    </li>
                </ul>
            </div>

            <div className={cx('grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]', isFetching && 'opacity-75 transition-opacity')} aria-busy={isFetching}>
                <SlotGrid
                    key={weekStart.toISOString()}
                    label={`Team availability, week of ${format(weekStart, 'd MMMM')}`}
                    columns={columns}
                    scrollToSlot={firstBusySlot(model.windows, data, weekStart)}
                    onActiveChange={setActive}
                    renderCell={({ column, slot }) => {
                        const count = model.playersAt(column, slot);
                        const coaches = model.freeAt(column, slot).length - count;
                        // The number is printed where it changes, so a block reads as one stretch.
                        const above = slot > 0 ? model.playersAt(column, slot - 1) : 0;
                        const showNumber = count > 0 && above !== count;
                        return {
                            className: cx(heatClass(count), count > 0 && heatClass(above) === heatClass(count) && 'joins-above'),
                            content: showNumber ? <span aria-hidden="true">{count}</span> : undefined,
                            description: `${count} of ${players.length} players free${count >= needed ? ', enough for a lineup' : ''}${coaches > 0 ? `, plus ${coaches === 1 ? 'a coach' : `${coaches} coaches`}` : ''}`,
                        };
                    }}
                />

                <aside className="rounded-lg border border-line bg-surface p-4 lg:sticky lg:top-4 lg:self-start" aria-label="Details">
                    {active ? (
                        <SlotDetails
                            data={data}
                            start={slotStart(active.column, active.slot)}
                            slot={active.slot}
                            freeIds={model.freeAt(active.column, active.slot)}
                        />
                    ) : (
                        <WeekSummary data={data} windows={model.windows} />
                    )}
                </aside>
            </div>
        </div>
    );
}

/** Scroll to the first lineup window of the week, or else the first time anyone is free, or the evening. */
function firstBusySlot(windows: { start: Date }[], data: TeamAvailability, weekStart: Date) {
    const from = weekStart.getTime();
    const firstWindow = windows.find(w => w.start.getTime() >= from);
    const first = firstWindow?.start ?? (data.slots[0] ? new Date(data.slots[0].start) : null);
    if (!first) return 36;
    return first.getHours() * 2 + Math.floor(first.getMinutes() / 30);
}

function SlotDetails({ data, start, slot, freeIds }: { data: TeamAvailability; start: Date; slot: number; freeIds: string[] }) {
    const free = new Set(freeIds);
    const players = data.members.filter(m => m.countsTowardsLineup);
    const freePlayers = players.filter(m => free.has(m.userId));
    const busyPlayers = players.filter(m => !free.has(m.userId));
    const coaches = data.members.filter(m => !m.countsTowardsLineup);
    const enough = freePlayers.length >= data.playersNeeded;

    return (
        <div className="flex flex-col gap-4 text-sm">
            <div>
                <h3 className="font-display text-base font-black text-fg">{format(start, 'EEEE d MMM')}</h3>
                <p className="text-fg-muted tabular-nums">{slotTime(slot)}–{slotTime(slot + 1)}</p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
                <p className="text-fg"><strong className="text-2xl font-black tabular-nums">{freePlayers.length}</strong> of {players.length} players free</p>
                {enough
                    ? <Badge tone="success" icon={<Check className="size-3.5" aria-hidden="true" />}>Lineup ready</Badge>
                    : <Badge>{data.playersNeeded - freePlayers.length} more needed</Badge>}
            </div>
            <PeopleList title="Free" people={freePlayers.map(m => m.username)} empty="Nobody yet." />
            <PeopleList title="Not free" people={busyPlayers.map(m => (m.hasFilledIn ? m.username : `${m.username} (not filled in)`))} empty="Everyone is free." />
            {coaches.length > 0 && (
                <PeopleList
                    title="Coaches, not counted"
                    people={coaches.map(m => `${m.username}: ${free.has(m.userId) ? 'free' : 'not free'}`)}
                    empty=""
                />
            )}
        </div>
    );
}

function WeekSummary({ data, windows }: { data: TeamAvailability; windows: ReturnType<typeof lineupWindows> }) {
    const waitingOn = data.members.filter(m => m.countsTowardsLineup && !m.hasFilledIn).map(m => m.username);

    return (
        <div className="flex flex-col gap-4 text-sm">
            <div>
                <h3 className="font-display text-base font-black text-fg">Lineup windows</h3>
                <p className="text-fg-muted">When {data.playersNeeded} or more players are free together. Point at a time for who's in.</p>
            </div>
            {windows.length === 0 ? (
                <p className="text-fg-muted">No time this week has {data.playersNeeded} players free yet.</p>
            ) : (
                <ul className="flex flex-col divide-y divide-line">
                    {windows.map(window => (
                        <li key={window.start.toISOString()} className="flex items-baseline justify-between gap-3 py-2 first:pt-0 last:pb-0">
                            <span>
                                <span className="font-semibold text-fg">{format(window.start, 'EEE d MMM')}</span>{' '}
                                <span className="text-fg-muted tabular-nums">{windowTimes(window)}</span>
                            </span>
                            <span className="flex shrink-0 items-center gap-1 font-bold text-success tabular-nums" title={listNames(window.players.map(p => p.username))}>
                                <UsersRound className="size-3.5" aria-hidden="true" />
                                {window.playersFree}
                                <span className="sr-only"> players: {listNames(window.players.map(p => p.username))}</span>
                            </span>
                        </li>
                    ))}
                </ul>
            )}
            <div className="border-t border-line pt-4">
                {waitingOn.length === 0
                    ? <p className="text-fg-muted">Every player has filled in their availability.</p>
                    : <p className="text-fg-muted"><span className="font-semibold text-fg">Not filled in yet:</span> {listNames(waitingOn)}.</p>}
            </div>
        </div>
    );
}

function PeopleList({ title, people, empty }: { title: string; people: string[]; empty: string }) {
    if (people.length === 0 && !empty) return null;
    return (
        <div>
            <h4 className="mb-1 text-xs font-semibold tracking-wide text-fg-subtle uppercase">{title}</h4>
            {people.length === 0 ? (
                <p className="text-fg-muted">{empty}</p>
            ) : (
                <ul className="flex flex-col gap-0.5 text-fg">
                    {people.map(name => <li key={name} className="truncate">{name}</li>)}
                </ul>
            )}
        </div>
    );
}
