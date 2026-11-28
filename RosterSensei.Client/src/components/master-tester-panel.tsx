import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { FlaskConical, Mail, Plus, RefreshCw, Sparkles } from 'lucide-react';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { type Team } from '../types';
import { Alert, Badge, Button, Field, Input, Modal, useToast } from './ui';

// Test data tools for the master tester account. The API answers 404 to everyone else, and in
// Production, so for them this renders nothing. See MasterTesterController.

type Section = 'roster' | 'matches' | 'calendar' | 'offdays' | 'availability' | 'mappool';
type Mode = 'add' | 'replace';

interface SeedResult {
    teamId: number | null;
    message: string;
}

const SECTIONS: { id: Section; label: string; description: string }[] = [
    { id: 'roster', label: 'Roster', description: 'Adds 6 made-up players, one of them an Officer. Replace keeps only the Owner.' },
    { id: 'matches', label: 'Matches', description: '14 results with maps, half scores and lineups, plus 5 scheduled (one needs a result).' },
    { id: 'calendar', label: 'Calendar', description: 'Praccs, scrims, theory, VOD reviews and official matches, last month to next month.' },
    { id: 'offdays', label: 'Off days', description: '6 off days in the same range. Events on them show as cancelled.' },
    { id: 'availability', label: 'Availability', description: 'A usual week and a few changed days for each made-up player, never for you. They are shared by every test team, so Replace changes them everywhere.' },
    { id: 'mappool', label: 'Map pool', description: 'T and CT ratings from every member. Add only fills the missing ones.' },
];

// Which sections the page being viewed shows, so they're listed first.
const SECTIONS_BY_PAGE: Record<string, Section[]> = {
    '/dashboard': ['matches', 'roster'],
    '/team': ['roster'],
    '/calendar': ['calendar', 'offdays', 'availability'],
    '/availability': ['availability'],
    '/matches': ['matches'],
    '/map-pool': ['mappool'],
    '/statistics': ['matches'],
};

const timeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;

export interface MasterTesterPanelProps {
    selectedTeam: Team | null;
    /** A team was created: select it and reload. */
    onTeamCreated: (teamId: number) => void;
    /** Data changed: reload the page being viewed. */
    onDataChanged: () => void;
}

export default function MasterTesterPanel(props: MasterTesterPanelProps) {
    const [available, setAvailable] = useState(false);

    useEffect(() => {
        const controller = new AbortController();
        api.get('/master-tester', { signal: controller.signal })
            .then(() => setAvailable(true))
            .catch(() => setAvailable(false)); // 404 for everyone but the tester: nothing to show.
        return () => controller.abort();
    }, []);

    return available ? <TesterTools {...props} /> : null;
}

function TesterTools({ selectedTeam, onTeamCreated, onDataChanged }: MasterTesterPanelProps) {
    const { pathname } = useLocation();
    const toast = useToast();
    const action = useAction();
    const [open, setOpen] = useState(false);
    const [teamName, setTeamName] = useState('');
    // Which button started the running action, so only that one shows a spinner.
    const [activeKey, setActiveKey] = useState<string | null>(null);

    const pageSections = Object.entries(SECTIONS_BY_PAGE).find(([path]) => pathname.startsWith(path))?.[1] ?? [];
    const orderedSections = [...SECTIONS].sort((a, b) => Number(pageSections.includes(b.id)) - Number(pageSections.includes(a.id)));

    const isLoading = (key: string) => action.isRunning && activeKey === key;

    const run = async (key: string, request: () => Promise<SeedResult>, failure: string) => {
        setActiveKey(key);
        const result = await action.run(request, failure);
        if (!result.ok) return;
        toast.success(result.data.message);
        if (key === 'create' && result.data.teamId) {
            setTeamName('');
            onTeamCreated(result.data.teamId);
        } else {
            onDataChanged();
        }
    };

    const createTeam = (event: FormEvent) => {
        event.preventDefault();
        void run(
            'create',
            () => api.post<SeedResult>('/master-tester/teams', { name: teamName.trim() || null, timeZone: timeZone() }).then(r => r.data),
            'Failed to create the team.',
        );
    };

    const fill = (section: Section, mode: Mode) => {
        if (!selectedTeam) return;
        void run(
            `${section}:${mode}`,
            () => api.post<SeedResult>(`/master-tester/teams/${selectedTeam.id}/${section}`, { mode, timeZone: timeZone() }).then(r => r.data),
            'Failed to fill this section.',
        );
    };

    const sendInvites = (mode: Mode) =>
        void run(
            `invites:${mode}`,
            () => api.post<SeedResult>('/master-tester/invites', { mode }).then(r => r.data),
            'Failed to send the invites.',
        );

    return (
        <>
            <button
                type="button"
                onClick={() => { action.reset(); setOpen(true); }}
                className="fixed right-4 bottom-20 z-40 inline-flex h-11 items-center gap-2 rounded-full border border-line-strong bg-raised px-4 text-sm font-semibold text-fg shadow-lg transition-colors hover:bg-hover lg:bottom-6"
            >
                <FlaskConical className="size-4 text-accent-text" aria-hidden="true" />
                Tester
            </button>

            <Modal
                open={open}
                onClose={() => setOpen(false)}
                title="Master tester"
                description="Made-up test data, generated in one click. Only your account can see this panel."
                size="lg"
                busy={action.isRunning}
            >
                <div className="flex flex-col gap-6">
                    {action.error && <Alert tone="danger">{action.error}</Alert>}

                    <section aria-labelledby="tester-new-team" className="flex flex-col gap-3">
                        <h3 id="tester-new-team" className="text-sm font-bold text-fg">New team</h3>
                        <form onSubmit={createTeam} className="flex flex-col gap-2 sm:flex-row sm:items-end">
                            <Field label="Team name" optional className="flex-1">
                                <Input value={teamName} onChange={e => setTeamName(e.target.value)} maxLength={100} placeholder="Made up when left empty" />
                            </Field>
                            <Button type="submit" loading={isLoading('create')} disabled={action.isRunning && !isLoading('create')} leftIcon={<Sparkles className="size-4" aria-hidden="true" />}>
                                Create full team
                            </Button>
                        </form>
                    </section>

                    <section aria-labelledby="tester-sections" className="flex flex-col gap-3">
                        <h3 id="tester-sections" className="text-sm font-bold text-fg">
                            {selectedTeam ? <>Fill <span className="text-accent-text">{selectedTeam.name}</span></> : 'Fill a team'}
                        </h3>
                        {!selectedTeam ? (
                            <p className="text-sm text-fg-muted">Create or join a team first.</p>
                        ) : (
                            <ul className="flex flex-col divide-y divide-line rounded-md border border-line">
                                {orderedSections.map(section => (
                                    <SectionRow
                                        key={section.id}
                                        label={section.label}
                                        description={section.description}
                                        onThisPage={pageSections.includes(section.id)}
                                        busy={action.isRunning}
                                        isLoading={mode => isLoading(`${section.id}:${mode}`)}
                                        onRun={mode => fill(section.id, mode)}
                                    />
                                ))}
                            </ul>
                        )}
                    </section>

                    <section aria-labelledby="tester-invites" className="flex flex-col gap-3">
                        <h3 id="tester-invites" className="text-sm font-bold text-fg">Your account</h3>
                        <ul className="flex flex-col rounded-md border border-line">
                            <SectionRow
                                label="Invites"
                                icon={<Mail className="size-4" aria-hidden="true" />}
                                description="2 pending invites from made-up teams, for testing the bell. Replace cancels the old ones."
                                busy={action.isRunning}
                                isLoading={mode => isLoading(`invites:${mode}`)}
                                onRun={sendInvites}
                            />
                        </ul>
                    </section>
                </div>
            </Modal>
        </>
    );
}

interface SectionRowProps {
    label: string;
    description: string;
    icon?: ReactNode;
    onThisPage?: boolean;
    busy: boolean;
    isLoading: (mode: Mode) => boolean;
    onRun: (mode: Mode) => void;
}

function SectionRow({ label, description, icon, onThisPage = false, busy, isLoading, onRun }: SectionRowProps) {
    return (
        <li className="flex flex-col gap-3 p-3 sm:flex-row sm:items-center">
            <div className="min-w-0 flex-1">
                <p className="flex items-center gap-2 text-sm font-semibold text-fg">
                    {icon}
                    {label}
                    {onThisPage && <Badge tone="accent">This tab</Badge>}
                </p>
                <p className="mt-0.5 text-sm text-fg-muted">{description}</p>
            </div>
            <div className="flex shrink-0 gap-2">
                <Button size="sm" variant="secondary" aria-label={`Add ${label.toLowerCase()}`} loading={isLoading('add')} disabled={busy && !isLoading('add')} leftIcon={<Plus className="size-4" aria-hidden="true" />} onClick={() => onRun('add')}>
                    Add
                </Button>
                <Button size="sm" variant="danger" aria-label={`Replace ${label.toLowerCase()}`} loading={isLoading('replace')} disabled={busy && !isLoading('replace')} leftIcon={<RefreshCw className="size-4" aria-hidden="true" />} onClick={() => onRun('replace')}>
                    Replace
                </Button>
            </div>
        </li>
    );
}
