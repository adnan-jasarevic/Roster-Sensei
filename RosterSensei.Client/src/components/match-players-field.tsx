import { useCallback } from 'react';
import api from '../services/api';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { parsePlayerRating } from '../lib/player-ratings';
import { LINEUP_MAX_PLAYERS, type TeamMember } from '../types';
import { Checkbox, ErrorState, Field, Input, LoadingRegion, Skeleton } from './ui';

/** A player ticked as having played, with the rating as typed (empty means not rated). */
export interface PlayerPick {
    userId: string;
    rating: string;
}

interface MatchPlayersFieldProps {
    teamId: number;
    value: PlayerPick[];
    onChange: (picks: PlayerPick[]) => void;
    /** Show rating errors, after a save was tried. */
    showErrors: boolean;
}

/**
 * Who played a finished match, with an optional rating each. Part of Add Match; the Lineup dialog
 * covers roles and substitutes later.
 */
export default function MatchPlayersField({ teamId, value, onChange, showErrors }: MatchPlayersFieldProps) {
    const loadMembers = useCallback<ResourceLoader<TeamMember[]>>(
        signal => api.get<TeamMember[]>(`/Teams/${teamId}/members`, { signal }).then(response => response.data),
        [teamId],
    );
    const { data: members, error, isLoading, isFetching, reload } = useResource(loadMembers, 'Failed to load the roster.');

    const isFull = value.length >= LINEUP_MAX_PLAYERS;
    const pickFor = (userId: string) => value.find(p => p.userId === userId);

    const toggle = (userId: string) =>
        onChange(pickFor(userId) ? value.filter(p => p.userId !== userId) : [...value, { userId, rating: '' }]);

    const setRating = (userId: string, rating: string) =>
        onChange(value.map(p => (p.userId === userId ? { ...p, rating } : p)));

    return (
        <fieldset>
            <legend className="mb-1.5 text-sm font-medium text-fg-muted">
                Players <span className="font-normal text-fg-subtle">(optional: tick who played, ratings from 0.01 to 99.99)</span>
            </legend>

            {isLoading ? (
                <LoadingRegion label="Loading the roster" className="space-y-2">
                    {Array.from({ length: 3 }, (_, index) => <Skeleton key={index} className="h-11 w-full" />)}
                </LoadingRegion>
            ) : error && !members ? (
                <ErrorState compact title="Couldn't load the roster" message={error} onRetry={reload} retrying={isFetching} />
            ) : (
                <ul className="flex flex-col divide-y divide-line rounded-md border border-line">
                    {(members ?? []).map(member => {
                        const pick = pickFor(member.id);
                        const parsed = pick ? parsePlayerRating(pick.rating) : null;
                        const ratingError = showErrors && parsed && !parsed.ok ? parsed.message : undefined;
                        return (
                            <li key={member.id} className="flex flex-col gap-2 p-2 sm:flex-row sm:items-start">
                                <Checkbox
                                    label={<span className="truncate font-semibold text-fg">{member.username}</span>}
                                    aria-label={`${member.username} played`}
                                    checked={!!pick}
                                    disabled={!pick && isFull}
                                    onChange={() => toggle(member.id)}
                                    className="min-w-0 sm:mt-1.5 sm:flex-1"
                                />
                                {pick && (
                                    <Field label={`Rating of ${member.username}`} hideLabel error={ratingError} className="pl-8 sm:w-36 sm:pl-0">
                                        <Input
                                            value={pick.rating}
                                            onChange={e => setRating(member.id, e.target.value)}
                                            inputMode="decimal"
                                            placeholder="Rating, e.g. 1.15"
                                            autoComplete="off"
                                            className="h-9"
                                        />
                                    </Field>
                                )}
                            </li>
                        );
                    })}
                </ul>
            )}
            {isFull && <p className="mt-1.5 text-xs text-fg-subtle">A lineup holds at most {LINEUP_MAX_PLAYERS} players.</p>}
        </fieldset>
    );
}
