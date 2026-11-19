import { useId } from 'react';
import { parseStat, STAT_KEYS, STAT_LABELS, statsError, type StatKey, type TypedStats } from '../lib/match-stats';
import { Input } from './ui';

interface PlayerStatInputsProps {
    /** Names the inputs for screen readers: "Kills of nori". */
    username: string;
    value: TypedStats;
    onChange: (change: Partial<TypedStats>) => void;
    /** Mark invalid fields and say what's wrong, after a save was tried. */
    showErrors: boolean;
}

const PLACEHOLDERS: Record<StatKey, string> = { kills: '21', deaths: '15', assists: '4', adr: '85.0', headshotPercentage: '45.0' };

/** One player's scoreboard line: kills, deaths, assists, ADR and HS%, each optional. */
export default function PlayerStatInputs({ username, value, onChange, showErrors }: PlayerStatInputsProps) {
    const id = useId();
    const error = showErrors ? statsError(value) : undefined;
    const errorId = `${id}-error`;

    return (
        <div className="flex flex-col gap-1">
            <div className="grid grid-cols-3 gap-2 sm:grid-cols-5">
                {STAT_KEYS.map(key => {
                    const inputId = `${id}-${key}`;
                    const invalid = showErrors && !parseStat(key, value[key]).ok;
                    return (
                        <div key={key} className="flex min-w-0 flex-col gap-0.5">
                            <label htmlFor={inputId} className="text-xs font-medium text-fg-subtle" title={STAT_LABELS[key].long}>
                                <span aria-hidden="true">{STAT_LABELS[key].short}</span>
                                <span className="sr-only">{`${STAT_LABELS[key].long} of ${username}`}</span>
                            </label>
                            <Input
                                id={inputId}
                                value={value[key]}
                                onChange={e => onChange({ [key]: e.target.value })}
                                inputMode={key === 'adr' || key === 'headshotPercentage' ? 'decimal' : 'numeric'}
                                placeholder={PLACEHOLDERS[key]}
                                autoComplete="off"
                                aria-invalid={invalid || undefined}
                                aria-describedby={invalid ? errorId : undefined}
                                className="h-9 px-2 tabular-nums"
                            />
                        </div>
                    );
                })}
            </div>
            {error && <p id={errorId} className="text-xs font-medium text-danger">{error}</p>}
        </div>
    );
}
