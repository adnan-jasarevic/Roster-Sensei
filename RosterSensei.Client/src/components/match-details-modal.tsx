import { UsersRound } from 'lucide-react';
import { cx } from '../lib/cx';
import { formatDateTime } from '../lib/format';
import { formatPlayerRating } from '../lib/player-ratings';
import {
    formatDifference,
    formatOneDecimal,
    formatRatio,
    hasStats,
    killDeathRatio,
    teamTotals,
} from '../lib/match-stats';
import { hasResult, type LineupEntry, type Match } from '../types';
import { MapChip } from './map-chip';
import { MatchStatusBadge, OutcomeBadge } from './domain-badges';
import { Button, Modal } from './ui';

interface MatchDetailsModalProps {
    match: Match;
    onClose: () => void;
    /** Opens the Lineup dialog instead. Left out for members who can't change the lineup. */
    onEditLineup?: () => void;
}

const DASH = '–';
const show = (value: number | null | undefined, format: (v: number) => string = String) => (value == null ? DASH : format(value));

const COLUMNS: { label: string; title: string }[] = [
    { label: 'K', title: 'Kills' },
    { label: 'D', title: 'Deaths' },
    { label: 'A', title: 'Assists' },
    { label: '+/-', title: 'Kills minus deaths' },
    { label: 'K/D', title: 'Kills per death' },
    { label: 'ADR', title: 'Average damage per round' },
    { label: 'HS%', title: 'Headshot percentage' },
    { label: 'Rating', title: 'Match rating' },
];

const cell = 'border-b border-line px-3 py-2 text-right tabular-nums';

/** Everything about one match: result, maps, and the scoreboard of the players who played. */
export default function MatchDetailsModal({ match, onClose, onEditLineup }: MatchDetailsModalProps) {
    const isFinished = hasResult(match);
    const players = match.lineup.filter(p => p.played);
    const substitutes = match.lineup.filter(p => !p.played);
    const anyStats = players.some(hasStats);
    const totals = teamTotals(match.lineup);

    const details = [
        formatDateTime(match.playedDate ?? match.scheduledDate),
        match.type === 'Scrim' ? 'Scrim' : 'Official',
        match.tournament,
        match.bestOf && `Bo${match.bestOf}`,
    ].filter(Boolean).join(' · ');

    return (
        <Modal
            open
            onClose={onClose}
            title={`vs ${match.opponentName}`}
            description={details}
            size="lg"
            footer={
                <>
                    {onEditLineup && (
                        <Button variant="secondary" onClick={onEditLineup} leftIcon={<UsersRound className="size-4" aria-hidden="true" />}>
                            {match.lineup.length > 0 ? 'Edit lineup' : 'Set lineup'}
                        </Button>
                    )}
                    <Button variant="ghost" onClick={onClose}>Close</Button>
                </>
            }
        >
            <div className="flex flex-col gap-5">
                <div className="flex flex-wrap items-center gap-3">
                    {isFinished ? <OutcomeBadge outcome={match.matchOutcome} /> : <MatchStatusBadge status={match.status} />}
                    {isFinished && <span className="font-display text-2xl font-black text-fg tabular-nums">{match.finalScore}</span>}
                    {match.maps.length > 0 && (
                        <ul aria-label="Maps" className="flex flex-wrap gap-1.5 sm:ml-auto">
                            {match.maps.map((map, index) => <MapChip key={`${map.mapName}-${index}`} map={map} />)}
                        </ul>
                    )}
                </div>

                <section aria-labelledby={`scoreboard-${match.id}`} className="flex flex-col gap-2">
                    <h3 id={`scoreboard-${match.id}`} className="text-sm font-semibold text-fg">
                        {isFinished ? 'Scoreboard' : 'Expected lineup'}
                    </h3>
                    {players.length === 0 ? (
                        <p className="text-sm text-fg-subtle">No lineup recorded.</p>
                    ) : !isFinished ? (
                        <PlayerList players={players} />
                    ) : (
                        <>
                            <div className="overflow-x-auto rounded-md border border-line" tabIndex={0} role="region" aria-label="Scoreboard table">
                                <table className="w-full min-w-[36rem] border-separate border-spacing-0 text-sm">
                                    <caption className="sr-only">Stats of each player who played, with the team's totals in the last row.</caption>
                                    <thead className="bg-sunken">
                                        <tr>
                                            <th scope="col" className="sticky left-0 border-b border-line bg-sunken px-3 py-2 text-left font-semibold text-fg">Player</th>
                                            {COLUMNS.map(column => (
                                                <th key={column.label} scope="col" className="border-b border-line px-3 py-2 text-right font-semibold text-fg">
                                                    <abbr title={column.title} className="no-underline">{column.label}</abbr>
                                                </th>
                                            ))}
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {players.map(player => <ScoreboardRow key={player.userId} player={player} />)}
                                    </tbody>
                                    <tfoot className="bg-sunken font-semibold text-fg">
                                        <tr>
                                            <th scope="row" className="sticky left-0 bg-sunken px-3 py-2 text-left">Team</th>
                                            <td className={cx(cell, 'border-b-0')}>{show(totals.kills)}</td>
                                            <td className={cx(cell, 'border-b-0')}>{show(totals.deaths)}</td>
                                            <td className={cx(cell, 'border-b-0')}>{show(totals.assists)}</td>
                                            <td className={cx(cell, 'border-b-0')}>
                                                {totals.kills != null && totals.deaths != null ? formatDifference(totals.kills - totals.deaths) : DASH}
                                            </td>
                                            <td className={cx(cell, 'border-b-0')}>{show(totals.killDeath, formatRatio)}</td>
                                            <td className={cx(cell, 'border-b-0')}>{show(totals.adr, formatOneDecimal)}</td>
                                            <td className={cx(cell, 'border-b-0')}>{show(totals.headshotPercentage, v => `${formatOneDecimal(v)}%`)}</td>
                                            <td className={cx(cell, 'border-b-0')}>{show(totals.rating, formatPlayerRating)}</td>
                                        </tr>
                                    </tfoot>
                                </table>
                            </div>
                            <p className="text-xs text-fg-subtle">
                                {anyStats
                                    ? 'Kills, deaths and assists are added up; ADR, HS% and rating are the average of the players.'
                                    : 'No stats recorded yet. They can be added in the lineup.'}
                            </p>
                        </>
                    )}
                    {substitutes.length > 0 && (
                        <p className="text-sm text-fg-muted">
                            <span className="font-medium text-fg">Substitutes:</span> {substitutes.map(s => s.username + (s.role ? ` (${s.role})` : '')).join(', ')}
                        </p>
                    )}
                </section>

                {match.notes && (
                    <section aria-labelledby={`notes-${match.id}`} className="flex flex-col gap-1">
                        <h3 id={`notes-${match.id}`} className="text-sm font-semibold text-fg">Notes</h3>
                        <p className="text-sm whitespace-pre-line break-words text-fg-muted">{match.notes}</p>
                    </section>
                )}
            </div>
        </Modal>
    );
}

function ScoreboardRow({ player }: { player: LineupEntry }) {
    const { kills, deaths } = player;
    const hasKillsAndDeaths = kills != null && deaths != null;
    const difference = hasKillsAndDeaths ? kills - deaths : null;

    return (
        <tr className="hover:bg-hover">
            {/* Stays in view while the numbers scroll sideways on a phone. */}
            <th scope="row" className="sticky left-0 border-b border-line bg-raised px-3 py-2 text-left font-semibold whitespace-nowrap text-fg">
                {player.username}
                {player.role && <span className="ml-1.5 text-xs font-normal text-fg-subtle">{player.role}</span>}
            </th>
            <td className={cell}>{show(kills)}</td>
            <td className={cell}>{show(deaths)}</td>
            <td className={cell}>{show(player.assists)}</td>
            <td className={cx(cell, difference != null && difference > 0 && 'text-success', difference != null && difference < 0 && 'text-danger')}>
                {difference == null ? DASH : formatDifference(difference)}
            </td>
            <td className={cell}>{hasKillsAndDeaths ? formatRatio(killDeathRatio(kills, deaths)) : DASH}</td>
            <td className={cell}>{show(player.adr, formatOneDecimal)}</td>
            <td className={cell}>{show(player.headshotPercentage, v => `${formatOneDecimal(v)}%`)}</td>
            <td className={cx(cell, 'font-bold text-fg')}>{show(player.rating, formatPlayerRating)}</td>
        </tr>
    );
}

function PlayerList({ players }: { players: LineupEntry[] }) {
    return (
        <ul className="flex flex-wrap gap-1.5">
            {players.map(player => (
                <li key={player.userId} className="rounded border border-line bg-sunken px-2 py-1 text-xs text-fg">
                    <span className="font-semibold">{player.username}</span>
                    {player.role && <span className="ml-1.5 text-fg-muted">{player.role}</span>}
                </li>
            ))}
        </ul>
    );
}
