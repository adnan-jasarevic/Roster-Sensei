import { useCallback, useState } from 'react';
import { CalendarPlus, Swords, UsersRound } from 'lucide-react';
import api from '../services/api';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { cx } from '../lib/cx';
import { formatDateTime } from '../lib/format';
import { type LineupEntry, type Match, type MatchMap, type Team, canManageTeam, hasResult, isUpcoming, needsResult } from '../types';
import AddMatchModal from '../components/add-match-modal';
import LineupModal from '../components/lineup-modal';
import { MatchStatusBadge, NeedsResultBadge, OutcomeBadge } from '../components/domain-badges';
import PageHeader, { PageContainer } from '../components/page-header';
import { ListSkeleton } from '../components/page-skeletons';
import TeamGate from '../components/team-gate';
import { Alert, Button, Card, EmptyState, ErrorState, SegmentedControl, useToast } from '../components/ui';

type Filter = 'all' | 'upcoming' | 'needsResult' | 'results';

/** The dialog on screen. Only one can be open at a time. */
type Dialog =
    | { kind: 'none' }
    | { kind: 'addMatch' }
    | { kind: 'lineup'; matchId: number };

const NO_DIALOG: Dialog = { kind: 'none' };

const FILTERS: { value: Filter; label: string }[] = [
    { value: 'all', label: 'All' },
    { value: 'upcoming', label: 'Upcoming' },
    { value: 'needsResult', label: 'Needs result' },
    { value: 'results', label: 'Results' },
];

const EMPTY_TITLES: Record<Filter, string> = {
    all: 'No matches yet',
    upcoming: 'Nothing scheduled',
    needsResult: 'Nothing is waiting for a result',
    results: 'No results yet',
};

// "7-5, 6-6 +OT" style summary of a map's halves, or empty when halves weren't recorded.
const halvesText = (map: MatchMap) => {
    if (map.ourFirstHalf == null || map.opponentFirstHalf == null || map.ourSecondHalf == null || map.opponentSecondHalf == null)
        return '';
    const regulation = map.ourFirstHalf + map.ourSecondHalf + map.opponentFirstHalf + map.opponentSecondHalf;
    const total = (map.ourScore ?? 0) + (map.opponentScore ?? 0);
    const overtime = total > regulation ? ' +OT' : '';
    return `${map.ourFirstHalf}-${map.opponentFirstHalf}, ${map.ourSecondHalf}-${map.opponentSecondHalf}${overtime}`;
};

export default function Matches() {
    return (
        <TeamGate title="Matches">
            {team => <MatchesContent key={team.id} team={team} />}
        </TeamGate>
    );
}

function MatchesContent({ team }: { team: Team }) {
    const load = useCallback<ResourceLoader<Match[]>>(
        signal => api.get<Match[]>(`/Matches/${team.id}`, { signal }).then(response => response.data),
        [team.id],
    );
    const { data: matches, error, isLoading, isFetching, reload } = useResource(load, 'Failed to load matches.');
    const [filter, setFilter] = useState<Filter>('all');
    const [dialog, setDialog] = useState<Dialog>(NO_DIALOG);
    const closeDialog = () => setDialog(NO_DIALOG);
    const openAddMatch = () => setDialog({ kind: 'addMatch' });
    const canManageMatches = canManageTeam(team);
    const toast = useToast();
    // The time the page was opened. Scheduled matches before it are waiting for a result; the rest are upcoming.
    const [now] = useState(() => new Date());

    // Looked up in the loaded list, so the lineup dialog closes by itself if its match is gone after a reload.
    const lineupMatch = dialog.kind === 'lineup' ? matches?.find(m => m.id === dialog.matchId) : undefined;

    // The API returns matches newest first.
    const visibleMatches = (matches ?? []).filter(m => {
        if (filter === 'upcoming') return isUpcoming(m, now);
        if (filter === 'needsResult') return needsResult(m, now);
        if (filter === 'results') return hasResult(m);
        return true;
    });

    return (
        <PageContainer>
            <PageHeader
                title="Matches"
                description="Every match your team has scheduled or played."
                actions={canManageMatches && (
                    <Button onClick={openAddMatch} leftIcon={<Swords className="size-4" aria-hidden="true" />}>Add match</Button>
                )}
            />

            <SegmentedControl label="Show" hideLabel value={filter} options={FILTERS} onChange={setFilter} className="mb-4" />

            {error && matches && <Alert tone="danger" className="mb-4">{error}</Alert>}

            <Card className="overflow-hidden">
                {isLoading ? (
                    <ListSkeleton rows={6} label="Loading matches" />
                ) : error && !matches ? (
                    <ErrorState title="Couldn't load matches" message={error} onRetry={reload} retrying={isFetching} />
                ) : visibleMatches.length === 0 ? (
                    <EmptyState
                        icon={<CalendarPlus />}
                        title={EMPTY_TITLES[filter]}
                        description="Matches appear here once they've been added."
                        action={canManageMatches ? <Button variant="secondary" onClick={openAddMatch}>Add a match</Button> : undefined}
                    />
                ) : (
                    <ul className="divide-y divide-line">
                        {visibleMatches.map(match => (
                            <MatchRow key={match.id} match={match} overdue={needsResult(match, now)} onEditLineup={canManageMatches ? () => setDialog({ kind: 'lineup', matchId: match.id }) : undefined} />
                        ))}
                    </ul>
                )}
            </Card>

            {dialog.kind === 'addMatch' && <AddMatchModal teamId={team.id} onClose={closeDialog} onCreated={reload} />}

            {/* Keyed by match, so opening another match never carries over picks from the last one. */}
            {lineupMatch && (
                <LineupModal
                    key={lineupMatch.id}
                    teamId={team.id}
                    match={lineupMatch}
                    onClose={closeDialog}
                    onSaved={() => {
                        closeDialog();
                        toast.success(`Lineup saved for the match vs ${lineupMatch.opponentName}.`);
                        reload();
                    }}
                    onMatchMissing={reload}
                />
            )}
        </PageContainer>
    );
}

function MatchRow({ match, overdue, onEditLineup }: { match: Match; overdue: boolean; onEditLineup?: () => void }) {
    const isFinished = hasResult(match);
    const lineup = match.lineup ?? [];
    const details = [
        formatDateTime(match.scheduledDate),
        match.type === 'Scrim' && 'Scrim',
        match.tournament,
        match.bestOf && `Bo${match.bestOf}`,
    ].filter(Boolean).join(' · ');

    return (
        <li className="flex flex-col gap-3 p-4 lg:px-5">
            <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                <div className="flex min-w-0 items-center gap-3">
                    <span className="shrink-0">
                        {isFinished ? <OutcomeBadge outcome={match.matchOutcome} /> : overdue ? <NeedsResultBadge /> : <MatchStatusBadge status={match.status} />}
                    </span>
                    <div className="min-w-0">
                        <p className="truncate font-semibold text-fg">vs {match.opponentName}</p>
                        <p className="text-xs break-words text-fg-subtle">{details}</p>
                    </div>
                    {isFinished && <span className="ml-auto shrink-0 pl-2 font-display text-lg font-black whitespace-nowrap text-fg tabular-nums md:hidden">{match.finalScore}</span>}
                </div>

                <div className="flex min-w-0 items-center gap-4 md:justify-end">
                    {match.maps.length > 0 && (
                        <ul className="flex flex-wrap gap-1.5 md:justify-end">
                            {match.maps.map((map, index) => <MapChip key={`${map.mapName}-${index}`} map={map} />)}
                        </ul>
                    )}
                    {isFinished && <span className="hidden min-w-14 shrink-0 text-right font-display text-lg font-black whitespace-nowrap text-fg tabular-nums md:block">{match.finalScore}</span>}
                </div>
            </div>

            {(lineup.length > 0 || onEditLineup) && (
                <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
                    {lineup.length > 0 ? <Lineup lineup={lineup} opponentName={match.opponentName} /> : <p className="text-xs text-fg-subtle">No lineup recorded.</p>}
                    {onEditLineup && (
                        <Button
                            size="sm"
                            variant="ghost"
                            onClick={onEditLineup}
                            leftIcon={<UsersRound className="size-4" aria-hidden="true" />}
                            aria-label={`${lineup.length > 0 ? 'Edit' : 'Set'} lineup for the match vs ${match.opponentName}`}
                        >
                            {lineup.length > 0 ? 'Edit lineup' : 'Set lineup'}
                        </Button>
                    )}
                </div>
            )}
        </li>
    );
}

function MapChip({ map }: { map: MatchMap }) {
    const played = map.ourScore != null && map.opponentScore != null;
    const halves = halvesText(map);
    const scoreClass = !played || map.ourScore === map.opponentScore
        ? 'text-fg-muted'
        : map.ourScore! > map.opponentScore! ? 'text-success' : 'text-danger';

    return (
        <li className="rounded border border-line bg-sunken px-2 py-1 text-xs text-fg-muted">
            {map.mapName}
            {played && <span className={cx('ml-1.5 font-bold tabular-nums', scoreClass)}>{map.ourScore}-{map.opponentScore}</span>}
            {halves && <span className="ml-1.5 text-fg-subtle tabular-nums">({halves})</span>}
        </li>
    );
}

// Players who played, then substitutes, each with their role when one was recorded.
function Lineup({ lineup, opponentName }: { lineup: LineupEntry[]; opponentName: string }) {
    return (
        <ul aria-label={`Lineup vs ${opponentName}`} className="flex flex-wrap gap-1.5">
            {lineup.map(player => (
                <li
                    key={player.userId}
                    className={cx(
                        'rounded border px-2 py-1 text-xs',
                        player.played ? 'border-line bg-sunken text-fg' : 'border-dashed border-line-strong text-fg-subtle',
                    )}
                >
                    <span className="font-semibold">{player.username}</span>
                    {player.role && <span className="ml-1.5 text-fg-muted">{player.role}</span>}
                    {!player.played && <span className="ml-1.5">(sub, didn't play)</span>}
                </li>
            ))}
        </ul>
    );
}
