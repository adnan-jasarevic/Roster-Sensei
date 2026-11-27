import { useCallback, useEffect, useRef, useState, type ChangeEvent } from 'react';
import axios from 'axios';
import { FileUp, X } from 'lucide-react';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { DEMO_FILE_TYPES, DEMO_PARSER_VERSION, DemoReadError, demoMapName, readDemo } from '../lib/demo-reader';
import { formatOneDecimal } from '../lib/match-stats';
import type {
    ApplyDemoImportRequest,
    DemoImportPreview,
    DemoImportStatus,
    DemoSummary,
    Match,
    MatchCoachRequest,
    TeamMember,
} from '../types';
import { Alert, Button, Field, Input, Modal, SegmentedControl, Select } from './ui';

interface DemoImportModalProps {
    teamId: number;
    match: Match;
    onClose: () => void;
    /** Called with the match as the API saved it. */
    onSaved: (match: Match) => void;
    /** The match changed or is gone: the list should refresh. */
    onStale: () => void;
}

type Step =
    | { kind: 'pick' }
    | { kind: 'reading'; fileName: string; progress: number }
    | { kind: 'review'; demo: DemoSummary; preview: DemoImportPreview };

interface CoachPick {
    userId?: string;
    name?: string;
    steamId?: string;
    label: string;
}

const LEAVE_OUT = '';

const sideLabel = (players: { name: string }[]) =>
    players.length === 0 ? 'No players' : players.slice(0, 3).map(p => p.name).join(', ') + (players.length > 3 ? ', ...' : '');

/** Reads a demo on this device, shows what it holds, and saves its stats onto the match. */
export default function DemoImportModal({ teamId, match, onClose, onSaved, onStale }: DemoImportModalProps) {
    const loadMembers = useCallback<ResourceLoader<TeamMember[]>>(
        signal => api.get<TeamMember[]>(`/Teams/${teamId}/members`, { signal }).then(response => response.data),
        [teamId],
    );
    const { data: members } = useResource(loadMembers, 'Failed to load the roster.');

    const [step, setStep] = useState<Step>({ kind: 'pick' });
    const [error, setError] = useState<string>();
    const [ourSide, setOurSide] = useState(0);
    const [mapOrder, setMapOrder] = useState(1);
    const [links, setLinks] = useState<Record<string, string>>({});
    const [coaches, setCoaches] = useState<CoachPick[]>([]);
    const [newCoach, setNewCoach] = useState('');
    const save = useAction();
    const reading = useRef<AbortController | null>(null);
    const fileInput = useRef<HTMLInputElement>(null);

    // Stops a demo that is still being read when the dialog closes, so its worker and memory go too.
    useEffect(() => () => reading.current?.abort(), []);

    const isReading = step.kind === 'reading';
    const isSaving = save.isRunning;
    const players = members?.filter(m => m.role !== 'Coach') ?? [];

    const coachesFor = (preview: DemoImportPreview, side: number): CoachPick[] => {
        const saved = (match.coaches ?? []).map(c => ({ userId: c.userId ?? undefined, name: c.userId ? undefined : c.displayName, steamId: c.steamId ?? undefined, label: c.displayName }));
        const fromDemo = preview.sides[side].coaches
            .filter(c => !saved.some(s => s.steamId === c.steamId || (c.userId && s.userId === c.userId)))
            .map(c => ({ userId: c.userId ?? undefined, name: c.userId ? undefined : c.name, steamId: c.steamId, label: c.username ?? c.name }));
        return [...saved, ...fromDemo];
    };

    const chooseSide = (preview: DemoImportPreview, side: number) => {
        setOurSide(side);
        setLinks(Object.fromEntries(preview.sides[side].players.map(p => [p.steamId, p.userId && p.canPlay ? p.userId : LEAVE_OUT])));
        setCoaches(coachesFor(preview, side));
    };

    const handleFile = async (event: ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0];
        event.target.value = '';
        if (!file) return;
        setError(undefined);
        save.reset();

        try {
            const status = await api.get<DemoImportStatus>('/DemoImport/status', { params: { parserVersion: DEMO_PARSER_VERSION } });
            if (status.data.paused) {
                setError(status.data.message ?? 'Demo import is paused right now.');
                return;
            }
        } catch {
            setError("Couldn't reach the server. Try again in a moment.");
            return;
        }

        const controller = new AbortController();
        reading.current = controller;
        setStep({ kind: 'reading', fileName: file.name, progress: 0 });
        let demo: DemoSummary;
        try {
            demo = await readDemo(file, progress => setStep(s => (s.kind === 'reading' ? { ...s, progress } : s)), controller.signal);
        } catch (failure) {
            if (controller.signal.aborted) return;
            setStep({ kind: 'pick' });
            const message = failure instanceof Error ? failure.message : String(failure);
            let paused: string | null = null;
            if (failure instanceof DemoReadError && failure.patchVersion) {
                // Lets the server pause imports for everyone if this looks like a CS2 update.
                try {
                    const report = await api.post<DemoImportStatus>(`/DemoImport/${teamId}/failures`, { parserVersion: DEMO_PARSER_VERSION, patchVersion: failure.patchVersion });
                    paused = report.data.paused ? report.data.message : null;
                } catch {
                    // Reporting is best effort.
                }
            }
            setError(paused ?? `Couldn't read this demo: ${message}`);
            return;
        } finally {
            if (reading.current === controller) reading.current = null;
        }

        try {
            const response = await api.post<DemoImportPreview>(`/DemoImport/${teamId}/${match.id}/preview`, { demo });
            const preview = response.data;
            setMapOrder(preview.mapOrder);
            chooseSide(preview, preview.ourSide ?? 0);
            setStep({ kind: 'review', demo, preview });
        } catch (failure) {
            setStep({ kind: 'pick' });
            setError(axios.isAxiosError(failure) ? (failure.response?.data?.message ?? 'The server refused this demo.') : 'The server refused this demo.');
            if (axios.isAxiosError(failure) && failure.response?.status === 404) onStale();
        }
    };

    const cancelReading = () => {
        reading.current?.abort();
        setStep({ kind: 'pick' });
    };

    const addCoach = (value: string) => {
        const member = members?.find(m => m.id === value);
        if (member) {
            if (!coaches.some(c => c.userId === member.id)) setCoaches([...coaches, { userId: member.id, label: member.username }]);
            return;
        }
        const name = newCoach.trim();
        if (name && !coaches.some(c => !c.userId && c.label.toLowerCase() === name.toLowerCase())) {
            setCoaches([...coaches, { name, label: name }]);
        }
        setNewCoach('');
    };

    const handleImport = async () => {
        if (step.kind !== 'review') return;
        const body: ApplyDemoImportRequest = {
            demo: step.demo,
            ourSide,
            mapOrder,
            players: step.preview.sides[ourSide].players.map(p => ({ steamId: p.steamId, userId: links[p.steamId] || null })),
            coaches: coaches.map((c): MatchCoachRequest => ({ userId: c.userId, name: c.userId ? undefined : c.name, steamId: c.steamId })),
            version: match.version,
        };
        const result = await save.run(() => api.post<Match>(`/DemoImport/${teamId}/${match.id}`, body), 'Failed to import the demo.');
        if (result.ok) {
            onSaved(result.data.data);
            return;
        }
        if (result.httpStatus === 404 || result.httpStatus === 409) onStale();
    };

    const review = step.kind === 'review' ? step : null;
    const ours = review?.preview.sides[ourSide];
    const theirs = review?.preview.sides[1 - ourSide];
    const linkedCount = ours ? ours.players.filter(p => links[p.steamId]).length : 0;
    const maxMaps = match.bestOf ?? Math.max(1, match.maps.length);
    const mapOptions = [
        ...match.maps.map((map, index) => ({ value: index + 1, label: `Map ${index + 1}: ${map.mapName}` })),
        ...(match.maps.length < maxMaps ? [{ value: match.maps.length + 1, label: `Add as map ${match.maps.length + 1}` }] : []),
    ];
    const linkOptions = [{ value: LEAVE_OUT, label: 'Leave out' }, ...players.map(m => ({ value: m.id, label: m.username }))];
    const coachOptions = [{ value: '', label: 'Add a team member...' }, ...(members ?? []).filter(m => !coaches.some(c => c.userId === m.id)).map(m => ({ value: m.id, label: m.username }))];

    return (
        <Modal
            open
            onClose={() => (isReading ? cancelReading() : onClose())}
            title={`Import demo vs ${match.opponentName}`}
            description="The demo is read on this device and never uploaded. Only the scoreboard is saved."
            size={review ? 'xl' : 'md'}
            busy={isSaving}
            closeOnBackdrop={false}
            footer={
                <>
                    <Button variant="ghost" onClick={isReading ? cancelReading : onClose} disabled={isSaving}>Cancel</Button>
                    {review && (
                        <Button onClick={handleImport} loading={isSaving} loadingText="Importing..." disabled={linkedCount === 0}>
                            Import stats
                        </Button>
                    )}
                </>
            }
        >
            <div className="flex flex-col gap-4">
                {error && <Alert tone="danger">{error}</Alert>}
                {save.error && <Alert tone="danger">{save.error}</Alert>}

                {step.kind === 'pick' && (
                    <div className="flex flex-col items-start gap-3">
                        <p className="text-sm text-fg-muted">
                            Pick one map's CS2 demo: a .dem file, or a .gz, .bz2 or .zip holding one, up to 1 GB.
                            Reading a big demo can take a minute.
                        </p>
                        <input ref={fileInput} type="file" accept={DEMO_FILE_TYPES} className="sr-only" aria-label="Demo file" onChange={handleFile} />
                        <Button data-autofocus onClick={() => fileInput.current?.click()} leftIcon={<FileUp className="size-4" aria-hidden="true" />}>
                            Choose demo file
                        </Button>
                    </div>
                )}

                {step.kind === 'reading' && (
                    <div className="flex flex-col gap-2" aria-live="polite">
                        <p className="text-sm text-fg">Reading <span className="font-semibold break-all">{step.fileName}</span>...</p>
                        <progress className="h-2 w-full accent-[var(--color-accent,currentColor)]" max={1} value={step.progress} aria-label="Reading the demo" />
                        <p className="text-xs text-fg-subtle tabular-nums">{Math.round(step.progress * 100)}%</p>
                    </div>
                )}

                {review && ours && theirs && (
                    <>
                        <p className="text-sm text-fg">
                            <span className="font-semibold">{demoMapName(review.demo.mapName)}</span>
                            {' · '}
                            <span className="font-display text-lg font-black tabular-nums">{ours.score} - {theirs.score}</span>
                            {' · '}halves {ours.firstHalf}-{theirs.firstHalf} and {ours.secondHalf}-{theirs.secondHalf}
                            {' · '}{review.demo.rounds} rounds
                        </p>

                        {review.preview.ourSide == null && (
                            <Alert tone="warning">Nobody in this demo has linked the Steam account of a team member. Pick your side and link players by hand below.</Alert>
                        )}

                        <SegmentedControl
                            label="Your team in this demo"
                            value={ourSide}
                            options={[0, 1].map(side => ({ value: side, label: sideLabel(review.preview.sides[side].players) }))}
                            onChange={side => chooseSide(review.preview, side)}
                        />

                        <Field label="Map of this match" hint={review.preview.replacesImport && mapOrder === review.preview.mapOrder ? 'This map already has demo stats; they will be replaced.' : undefined}>
                            <Select value={mapOrder} options={mapOptions} onChange={setMapOrder} />
                        </Field>

                        <div className="overflow-x-auto rounded-md border border-line" tabIndex={0} role="region" aria-label="Players in the demo">
                            <table className="w-full min-w-[38rem] border-separate border-spacing-0 text-sm">
                                <caption className="sr-only">Your side's players from the demo, and who each one is on your team.</caption>
                                <thead className="bg-sunken">
                                    <tr>
                                        <th scope="col" className="border-b border-line px-3 py-2 text-left font-semibold">In the demo</th>
                                        <th scope="col" className="border-b border-line px-3 py-2 text-right font-semibold"><abbr title="Kills / deaths / assists" className="no-underline">K / D / A</abbr></th>
                                        <th scope="col" className="border-b border-line px-3 py-2 text-right font-semibold"><abbr title="Average damage per round" className="no-underline">ADR</abbr></th>
                                        <th scope="col" className="border-b border-line px-3 py-2 text-right font-semibold"><abbr title="Headshot percentage" className="no-underline">HS%</abbr></th>
                                        <th scope="col" className="border-b border-line px-3 py-2 text-left font-semibold">Team member</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {ours.players.map(player => (
                                        <tr key={player.steamId}>
                                            <th scope="row" className="border-b border-line px-3 py-2 text-left font-semibold whitespace-nowrap">{player.name}</th>
                                            <td className="border-b border-line px-3 py-2 text-right tabular-nums">{player.kills} / {player.deaths} / {player.assists}</td>
                                            <td className="border-b border-line px-3 py-2 text-right tabular-nums">{formatOneDecimal(player.adr)}</td>
                                            <td className="border-b border-line px-3 py-2 text-right tabular-nums">{formatOneDecimal(player.headshotPercentage)}%</td>
                                            <td className="border-b border-line px-2 py-1">
                                                <Select
                                                    size="sm"
                                                    aria-label={`Team member for ${player.name}`}
                                                    value={links[player.steamId] ?? LEAVE_OUT}
                                                    options={linkOptions.filter(o => o.value === LEAVE_OUT || o.value === links[player.steamId] || !Object.values(links).includes(o.value))}
                                                    onChange={value => setLinks({ ...links, [player.steamId]: value })}
                                                />
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                        <p className="text-xs text-fg-subtle">
                            {linkedCount} of {ours.players.length} players linked. Their kills, deaths, assists, ADR and HS% replace any typed in by hand; ratings stay.
                        </p>

                        <section aria-labelledby="demo-coaches" className="flex flex-col gap-2">
                            <h3 id="demo-coaches" className="text-sm font-semibold text-fg">Coaches</h3>
                            {coaches.length === 0 ? (
                                <p className="text-sm text-fg-subtle">No coach.</p>
                            ) : (
                                <ul aria-label="Coaches of this match" className="flex flex-wrap gap-1.5">
                                    {coaches.map((coach, index) => (
                                        <li key={`${coach.userId ?? coach.steamId ?? coach.label}-${index}`} className="flex items-center gap-1 rounded border border-line bg-sunken py-1 pr-1 pl-2 text-xs text-fg">
                                            <span className="font-semibold">{coach.label}</span>
                                            {!coach.userId && <span className="text-fg-subtle">(not on the team)</span>}
                                            <button type="button" className="rounded p-0.5 hover:bg-hover" aria-label={`Remove coach ${coach.label}`} onClick={() => setCoaches(coaches.filter((_, i) => i !== index))}>
                                                <X className="size-3.5" aria-hidden="true" />
                                            </button>
                                        </li>
                                    ))}
                                </ul>
                            )}
                            <div className="flex flex-wrap items-end gap-2">
                                <Select size="sm" className="w-full sm:w-56" aria-label="Add a team member as coach" value="" options={coachOptions} onChange={value => value && addCoach(value)} />
                                <Input className="w-full sm:w-56" aria-label="Name of a coach from outside the team" placeholder="Or type a name" value={newCoach} maxLength={64} onChange={e => setNewCoach(e.target.value)} />
                                <Button size="sm" variant="secondary" onClick={() => addCoach('')} disabled={!newCoach.trim()}>Add coach</Button>
                            </div>
                        </section>
                    </>
                )}
            </div>
        </Modal>
    );
}
