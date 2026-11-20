import { useState } from 'react';
import { format } from 'date-fns';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { MAP_POOL } from '../constants/maps';
import { formatPlayerRating, parsePlayerRating, ratingsAreValid } from '../lib/player-ratings';
import { statsOf } from '../lib/match-stats';
import {
    MatchKind,
    MatchStatus,
    hasResult,
    type CreateMatchRequest,
    type LineupPlayer,
    type Match,
    type MatchKindValue,
    type MatchMap,
    type MatchStatusValue,
    type UpdateMatchRequest,
} from '../types';
import { Alert, Button, Field, Input, Modal, SegmentedControl, Select, Textarea, ToggleChip } from './ui';
import MatchPlayersField, { type PlayerPick } from './match-players-field';

interface MatchModalProps {
    teamId: number;
    /** The match to edit. Without it, the dialog adds a new match. */
    match?: Match;
    /** Opens the edit with the status set to Finished: the "Add result" action. */
    recordResult?: boolean;
    onClose: () => void;
    /** Saved: closes the dialog. The message is for a toast. */
    onSaved: (message: string) => void;
    /** The list behind the dialog is out of date: the match was removed or changed by someone else. */
    onStale: () => void;
}

const TYPE_OPTIONS = [
    { value: MatchKind.Official, label: 'Official' },
    { value: MatchKind.Scrim, label: 'Scrim' },
];

const BEST_OF_OPTIONS = [1, 2, 3, 5].map(value => ({ value, label: `Bo${value}` }));

// Score inputs are kept as text so a field can be left empty (map not played, halves not tracked).
type ScoreField = 'ourScore' | 'opponentScore' | 'ourFirstHalf' | 'opponentFirstHalf' | 'ourSecondHalf' | 'opponentSecondHalf';
type MapScoreInput = Record<ScoreField, string>;

const EMPTY_MAP_SCORE: MapScoreInput = {
    ourScore: '',
    opponentScore: '',
    ourFirstHalf: '',
    opponentFirstHalf: '',
    ourSecondHalf: '',
    opponentSecondHalf: '',
};

const toNumber = (value: string) => (value.trim() === '' ? undefined : Math.max(0, Number(value)));
const toText = (value: number | null | undefined) => (value == null ? '' : String(value));

/** "2026-11-20T19:00" in the user's time zone, the format a datetime-local input takes. */
const toLocalInput = (iso: string) => format(new Date(iso), "yyyy-MM-dd'T'HH:mm");

/** The series score of a finished match, read from "13 - 9". */
const seriesScore = (match: Match) => {
    const [ours, theirs] = match.finalScore.split(' - ').map(Number);
    return { ours: Number.isFinite(ours) ? ours : 0, theirs: Number.isFinite(theirs) ? theirs : 0 };
};

/**
 * The lineup to send with an edit of a finished match. The dialog only shows who played and their
 * ratings, so everything else is kept as it was: roles, stats, substitutes and players who have left.
 */
function mergeLineup(match: Match, picks: PlayerPick[]): LineupPlayer[] {
    const existing = new Map(match.lineup.map(entry => [entry.userId, entry]));
    const picked = new Set(picks.map(p => p.userId));
    const played = picks.map(p => {
        const rating = parsePlayerRating(p.rating);
        return {
            userId: p.userId,
            role: existing.get(p.userId)?.role ?? undefined,
            played: true,
            rating: rating.ok && rating.value !== null ? rating.value : undefined,
            // Keep stats only for players who already played.
            ...(existing.get(p.userId)?.played ? statsOf(existing.get(p.userId)) : {}),
        };
    });
    const substitutes = match.lineup
        .filter(entry => !entry.played && !picked.has(entry.userId))
        .map(entry => ({ userId: entry.userId, role: entry.role ?? undefined, played: false }));
    return [...played, ...substitutes];
}

export default function MatchModal({ teamId, match, recordResult = false, onClose, onSaved, onStale }: MatchModalProps) {
    const isEdit = match !== undefined;
    const [opponentName, setOpponentName] = useState(match?.opponentName ?? '');
    const [tournament, setTournament] = useState(match?.tournament ?? '');
    const [scheduledDate, setScheduledDate] = useState(match ? toLocalInput(match.scheduledDate) : '');
    const [status, setStatus] = useState<MatchStatusValue>(
        recordResult ? MatchStatus.Finished : match ? MatchStatus[match.status] : MatchStatus.Scheduled,
    );
    const [type, setType] = useState<MatchKindValue>(match ? MatchKind[match.type] : MatchKind.Official);
    const [ourScore, setOurScore] = useState(match && hasResult(match) ? seriesScore(match).ours : 0);
    const [opponentScore, setOpponentScore] = useState(match && hasResult(match) ? seriesScore(match).theirs : 0);
    const [bestOf, setBestOf] = useState(match?.bestOf ?? 1);
    const [maps, setMaps] = useState<string[]>(match?.maps.map(m => m.mapName) ?? []);
    const [mapScores, setMapScores] = useState<Record<string, MapScoreInput>>(() => Object.fromEntries(
        (match?.maps ?? []).map(m => [m.mapName, {
            ourScore: toText(m.ourScore),
            opponentScore: toText(m.opponentScore),
            ourFirstHalf: toText(m.ourFirstHalf),
            opponentFirstHalf: toText(m.opponentFirstHalf),
            ourSecondHalf: toText(m.ourSecondHalf),
            opponentSecondHalf: toText(m.opponentSecondHalf),
        }]),
    ));
    const [notes, setNotes] = useState(match?.notes ?? '');
    // Everyone who played, with their rating. A scheduled match's lineup counts too: they're
    // the expected players, ready to be confirmed when the result goes in.
    const [players, setPlayers] = useState<PlayerPick[]>(() => (match?.lineup ?? [])
        .filter(entry => entry.played)
        .map(entry => ({ userId: entry.userId, rating: entry.rating != null ? formatPlayerRating(entry.rating) : '' })));

    const save = useAction();
    const isSubmitting = save.isRunning;
    const [showFieldErrors, setShowFieldErrors] = useState(false);

    const isFinished = status === MatchStatus.Finished;
    const hasMapScores = isFinished && maps.length > 0;

    const scoreFor = (map: string) => mapScores[map] ?? EMPTY_MAP_SCORE;

    const setMapScore = (map: string, field: ScoreField, value: string) => {
        setMapScores(current => ({ ...current, [map]: { ...(current[map] ?? EMPTY_MAP_SCORE), [field]: value } }));
    };

    const buildMap = (map: string): MatchMap => {
        const score = scoreFor(map);
        if (!isFinished) return { mapName: map };
        return {
            mapName: map,
            ourScore: toNumber(score.ourScore),
            opponentScore: toNumber(score.opponentScore),
            ourFirstHalf: toNumber(score.ourFirstHalf),
            opponentFirstHalf: toNumber(score.opponentFirstHalf),
            ourSecondHalf: toNumber(score.ourSecondHalf),
            opponentSecondHalf: toNumber(score.opponentSecondHalf),
        };
    };

    // Mirrors the API: a Bo1 shows the round score, a longer series shows maps won.
    const playedMaps = maps.map(buildMap).filter(m => m.ourScore != null && m.opponentScore != null);
    const seriesPreview = playedMaps.length === 0
        ? null
        : bestOf === 1 && playedMaps.length === 1
            ? `${playedMaps[0].ourScore} - ${playedMaps[0].opponentScore}`
            : `${playedMaps.filter(m => m.ourScore! > m.opponentScore!).length} - ${playedMaps.filter(m => m.opponentScore! > m.ourScore!).length}`;

    const toggleMap = (map: string) => {
        setMaps(current => {
            if (current.includes(map)) return current.filter(m => m !== map);
            if (current.length >= bestOf) return current;
            return [...current, map];
        });
    };

    const handleBestOfChange = (value: number) => {
        setBestOf(value);
        setMaps(current => current.slice(0, value));
    };

    const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
        e.preventDefault();

        // The button stays enabled so pressing it can explain what's missing, right on the fields.
        if (!opponentName.trim() || !scheduledDate || (isFinished && !ratingsAreValid(players))) {
            save.reset();
            setShowFieldErrors(true);
            return;
        }

        // datetime-local has no time zone; new Date() reads it as the user's local time and toISOString() sends UTC.
        const scheduledIso = new Date(scheduledDate).toISOString();

        const lineup: LineupPlayer[] | undefined = !isFinished
            // On an edit, leaving the lineup out keeps it (the API drops ratings from a match that isn't finished).
            ? undefined
            : isEdit
                ? mergeLineup(match, players)
                : players.length > 0
                    ? players.map(p => {
                        const rating = parsePlayerRating(p.rating);
                        return { userId: p.userId, played: true, rating: rating.ok && rating.value !== null ? rating.value : undefined };
                    })
                    : undefined;

        const payload: CreateMatchRequest = {
            opponentName: opponentName.trim(),
            tournament: tournament.trim() || undefined,
            scheduledDate: scheduledIso,
            playedDate: isFinished ? scheduledIso : undefined,
            status,
            type,
            ourScore: isFinished ? ourScore : 0,
            opponentScore: isFinished ? opponentScore : 0,
            bestOf,
            maps: maps.map(buildMap),
            notes: notes.trim() || undefined,
            lineup,
        };

        if (!isEdit) {
            const result = await save.run(() => api.post(`/Matches/${teamId}`, payload), 'Failed to add the match.');
            if (result.ok) onSaved(`Match vs ${payload.opponentName} added.`);
            return;
        }

        const body: UpdateMatchRequest = { ...payload, version: match.version };
        const result = await save.run(() => api.put<Match>(`/Matches/${teamId}/${match.id}`, body), 'Failed to save the match.');
        if (result.ok) {
            onSaved(recordResult ? `Result saved for the match vs ${payload.opponentName}.` : `Match vs ${payload.opponentName} saved.`);
            return;
        }
        // Removed, or changed by someone else: the dialog keeps the message, the list behind reloads.
        if (result.httpStatus === 404 || result.httpStatus === 409) onStale();
    };

    const title = !isEdit ? 'Add match' : recordResult ? `Add result vs ${match.opponentName}` : `Edit match vs ${match.opponentName}`;
    const submitLabel = !isEdit ? 'Add match' : recordResult ? 'Save result' : 'Save changes';

    return (
        <Modal
            open
            onClose={onClose}
            title={title}
            busy={isSubmitting}
            closeOnBackdrop={false}
            footer={
                <>
                    <Button variant="ghost" onClick={onClose} disabled={isSubmitting}>Cancel</Button>
                    <Button type="submit" form="match-form" loading={isSubmitting} loadingText="Saving...">{submitLabel}</Button>
                </>
            }
        >
            <form id="match-form" onSubmit={handleSubmit} noValidate className="flex flex-col gap-5">
                {save.error && <Alert tone="danger">{save.error}</Alert>}

                <SegmentedControl label="Type" value={type} options={TYPE_OPTIONS} onChange={setType} />

                <Field label="Opponent" required error={showFieldErrors && !opponentName.trim() ? 'Enter the opponent\'s name.' : undefined}>
                    <Input value={opponentName} onChange={(e) => setOpponentName(e.target.value)} placeholder="e.g. Tokyo Ronin" maxLength={100} autoComplete="off" data-autofocus />
                </Field>

                <Field label="Tournament" optional>
                    <Input value={tournament} onChange={(e) => setTournament(e.target.value)} placeholder="e.g. Local LAN Cup" maxLength={100} autoComplete="off" />
                </Field>

                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <Field label="Date and time" required error={showFieldErrors && !scheduledDate ? 'Pick a date and time.' : undefined}>
                        <Input type="datetime-local" value={scheduledDate} onChange={(e) => setScheduledDate(e.target.value)} />
                    </Field>
                    <Field label="Status">
                        <Select<MatchStatusValue>
                            value={status}
                            onChange={setStatus}
                            options={[
                                { value: MatchStatus.Scheduled, label: 'Scheduled' },
                                { value: MatchStatus.Finished, label: 'Finished' },
                                { value: MatchStatus.Cancelled, label: 'Cancelled' },
                            ]}
                        />
                    </Field>
                </div>

                {isFinished && !hasMapScores && (
                    <div className="grid grid-cols-2 gap-4">
                        <Field label="Our score">
                            <Input type="number" inputMode="numeric" min={0} value={ourScore} onChange={(e) => setOurScore(Math.max(0, Number(e.target.value)))} />
                        </Field>
                        <Field label="Opponent score">
                            <Input type="number" inputMode="numeric" min={0} value={opponentScore} onChange={(e) => setOpponentScore(Math.max(0, Number(e.target.value)))} />
                        </Field>
                    </div>
                )}

                <SegmentedControl label="Format" value={bestOf} options={BEST_OF_OPTIONS} onChange={handleBestOfChange} />

                <fieldset>
                    <legend className="mb-1.5 text-sm font-medium text-fg-muted">
                        Maps <span className="font-normal text-fg-subtle tabular-nums">({maps.length}/{bestOf}, in play order)</span>
                    </legend>
                    <div className="flex flex-wrap gap-2">
                        {MAP_POOL.map(map => {
                            const index = maps.indexOf(map);
                            const selected = index !== -1;
                            return (
                                <ToggleChip key={map} pressed={selected} disabled={!selected && maps.length >= bestOf} onToggle={() => toggleMap(map)}>
                                    {selected && <span className="mr-1 font-bold text-accent-text">{index + 1}.</span>}{map}
                                </ToggleChip>
                            );
                        })}
                    </div>
                </fieldset>

                {hasMapScores && (
                    <div className="flex flex-col gap-3">
                        <div className="flex items-baseline justify-between gap-4">
                            <span className="text-sm font-medium text-fg-muted">Map scores <span className="font-normal text-fg-subtle">(leave a map empty if it wasn't played)</span></span>
                            {seriesPreview && <span className="shrink-0 font-display font-black text-fg tabular-nums">{seriesPreview}</span>}
                        </div>
                        {maps.map((map, index) => {
                            const score = scoreFor(map);
                            const scoreInput = (field: ScoreField, label: string) => (
                                <Input
                                    type="number"
                                    inputMode="numeric"
                                    min={0}
                                    value={score[field]}
                                    onChange={(e) => setMapScore(map, field, e.target.value)}
                                    className="px-1 text-center"
                                    placeholder="-"
                                    aria-label={`${map} ${label}`}
                                />
                            );
                            return (
                                <fieldset key={map} className="rounded-md border border-line bg-sunken p-3">
                                    <legend className="px-1 text-sm font-bold text-fg">{index + 1}. {map}</legend>
                                    <div className="grid grid-cols-[auto_repeat(3,minmax(0,1fr))] items-center gap-2 text-xs text-fg-subtle">
                                        <span />
                                        <span className="text-center">Final</span>
                                        <span className="text-center">1st half</span>
                                        <span className="text-center">2nd half</span>
                                        <span>Us</span>
                                        {scoreInput('ourScore', 'our final score')}
                                        {scoreInput('ourFirstHalf', 'our first half')}
                                        {scoreInput('ourSecondHalf', 'our second half')}
                                        <span>Them</span>
                                        {scoreInput('opponentScore', 'opponent final score')}
                                        {scoreInput('opponentFirstHalf', 'opponent first half')}
                                        {scoreInput('opponentSecondHalf', 'opponent second half')}
                                    </div>
                                </fieldset>
                            );
                        })}
                        <p className="text-xs text-fg-subtle">Halves are optional. If the final score is higher than both halves added up, the map went to overtime.</p>
                    </div>
                )}

                {isFinished && <MatchPlayersField teamId={teamId} value={players} onChange={setPlayers} showErrors={showFieldErrors} />}

                <Field label="Notes" optional>
                    <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={1000} />
                </Field>
            </form>
        </Modal>
    );
}
