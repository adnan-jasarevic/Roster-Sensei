import { useCallback, useState, type FormEvent } from 'react';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import {
    LINEUP_MAX_PLAYERS,
    LINEUP_MAX_ROLE_LENGTH,
    type LineupEntry,
    type Match,
    type SetLineupRequest,
    type TeamMember,
} from '../types';
import { Alert, Button, Checkbox, ErrorState, Field, Input, LoadingRegion, Modal, Skeleton } from './ui';

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
}

export default function LineupModal({ teamId, match, onClose, onSaved, onMatchMissing }: LineupModalProps) {
    const loadMembers = useCallback<ResourceLoader<TeamMember[]>>(
        signal => api.get<TeamMember[]>(`/Teams/${teamId}/members`, { signal }).then(response => response.data),
        [teamId],
    );
    const { data: members, error: membersError, isLoading, isFetching, reload } = useResource(loadMembers, 'Failed to load the roster.');

    // In the order players were added, which the API keeps.
    const [picks, setPicks] = useState<Pick[]>(() =>
        (match.lineup ?? []).map(entry => ({ userId: entry.userId, role: entry.role ?? '', played: entry.played })),
    );
    const save = useAction();
    const isSaving = save.isRunning;

    const memberIds = new Set(members?.map(m => m.id));
    // Players in the saved lineup who have since left the team. The API only accepts current
    // members, so they are dropped when saving.
    const formerPlayers = members ? (match.lineup ?? []).filter(entry => !memberIds.has(entry.userId)) : [];
    const currentPicks = members ? picks.filter(p => memberIds.has(p.userId)) : picks;
    const isFull = currentPicks.length >= LINEUP_MAX_PLAYERS;
    const starters = currentPicks.filter(p => p.played).length;

    const pickFor = (userId: string) => picks.find(p => p.userId === userId);

    const toggle = (userId: string) => {
        setPicks(current => current.some(p => p.userId === userId)
            ? current.filter(p => p.userId !== userId)
            : [...current, { userId, role: '', played: true }]);
    };

    const update = (userId: string, change: Partial<Pick>) => {
        setPicks(current => current.map(p => (p.userId === userId ? { ...p, ...change } : p)));
    };

    const handleSubmit = async (event: FormEvent) => {
        event.preventDefault();
        if (!members) return;

        const body: SetLineupRequest = {
            players: currentPicks.map(p => ({
                userId: p.userId,
                role: p.role.trim() || undefined,
                played: p.played,
            })),
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
            description={`Pick up to ${LINEUP_MAX_PLAYERS} players. Untick "Played" for a substitute who didn't play. Roles are optional.`}
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

                    <ul className="flex flex-col divide-y divide-line rounded-md border border-line">
                        {(members ?? []).map(member => {
                            const pick = pickFor(member.id);
                            return (
                                <li key={member.id} className="flex flex-col gap-2 p-2 sm:flex-row sm:items-center">
                                    <Checkbox
                                        label={<span className="truncate font-semibold text-fg">{member.username}</span>}
                                        checked={!!pick}
                                        disabled={!pick && isFull}
                                        onChange={() => toggle(member.id)}
                                        className="min-w-0 sm:w-44 sm:shrink-0"
                                    />
                                    {pick && (
                                        <div className="flex min-w-0 flex-1 items-center gap-2 pl-8 sm:pl-0">
                                            <Field label={`Role of ${member.username}`} hideLabel className="flex-1">
                                                <Input
                                                    value={pick.role}
                                                    onChange={e => update(member.id, { role: e.target.value })}
                                                    maxLength={LINEUP_MAX_ROLE_LENGTH}
                                                    placeholder="Role, e.g. AWP"
                                                    autoComplete="off"
                                                    className="h-9"
                                                />
                                            </Field>
                                            <Checkbox
                                                label="Played"
                                                aria-label={`${member.username} played`}
                                                checked={pick.played}
                                                onChange={e => update(member.id, { played: e.target.checked })}
                                                className="shrink-0"
                                            />
                                        </div>
                                    )}
                                </li>
                            );
                        })}
                    </ul>
                    {isFull && <p className="text-xs text-fg-subtle">A lineup holds at most {LINEUP_MAX_PLAYERS} players. Untick someone to add another.</p>}
                </form>
            )}
        </Modal>
    );
}
