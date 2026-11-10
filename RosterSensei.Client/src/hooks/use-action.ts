import { useCallback, useRef, useState } from 'react';
import axios from 'axios';
import { getApiErrorMessage } from '../services/errors';

/** Where one user action (a save, a delete, a login) is in its life. */
export type ActionState =
    | { status: 'idle' }
    | { status: 'running' }
    | { status: 'failed'; error: string };

export type ActionResult<T> =
    | { ok: true; data: T }
    | {
        ok: false;
        /** What to tell the user. Empty when the call was ignored. */
        message: string;
        /** The HTTP status, when the server answered. */
        httpStatus?: number;
        /** True when the action was already running, so this call did nothing. */
        ignored?: true;
    };

/** A message, or a function that reads the error (for pages with their own wording, like login). */
export type FailureMessage = string | ((error: unknown) => string);

export interface Action {
    status: ActionState['status'];
    isRunning: boolean;
    /** The message of the last failed run. Cleared when the action runs again or is reset. */
    error: string | undefined;
    /** Runs the task unless one is already running. Never throws: check `ok` on the result. */
    run: <T>(task: () => Promise<T>, failure: FailureMessage) => Promise<ActionResult<T>>;
    /** Forgets a failure, e.g. when the dialog it was shown in opens again. */
    reset: () => void;
}

const IDLE: ActionState = { status: 'idle' };
const RUNNING: ActionState = { status: 'running' };

/**
 * One request started by the user, as a single state: idle, running or failed. It replaces a
 * hand-kept `isSaving` flag next to an `error` string, which can disagree with each other.
 *
 * What it guarantees: a second `run` while one is in flight is ignored (even in the same tick,
 * before React has re-rendered the button), the previous error is cleared when a new run starts,
 * and the action always leaves `running`, whether the task resolves or throws.
 *
 * ```tsx
 * const save = useAction();
 * const handleSubmit = async () => {
 *     const result = await save.run(() => api.post('/Teams', body), 'Failed to create the team.');
 *     if (result.ok) onClose();
 * };
 * <Modal busy={save.isRunning}>{save.error && <Alert tone="danger">{save.error}</Alert>}</Modal>
 * ```
 */
export function useAction(): Action {
    const [state, setState] = useState<ActionState>(IDLE);
    const inFlight = useRef(false);

    const run = useCallback(async <T,>(task: () => Promise<T>, failure: FailureMessage): Promise<ActionResult<T>> => {
        if (inFlight.current) return { ok: false, message: '', ignored: true };
        inFlight.current = true;
        setState(RUNNING);
        try {
            const data = await task();
            setState(IDLE);
            return { ok: true, data };
        } catch (error: unknown) {
            const message = typeof failure === 'function' ? failure(error) : getApiErrorMessage(error, failure);
            setState({ status: 'failed', error: message });
            return { ok: false, message, httpStatus: axios.isAxiosError(error) ? error.response?.status : undefined };
        } finally {
            inFlight.current = false;
        }
    }, []);

    const reset = useCallback(() => {
        if (!inFlight.current) setState(IDLE);
    }, []);

    return {
        status: state.status,
        isRunning: state.status === 'running',
        error: state.status === 'failed' ? state.error : undefined,
        run,
        reset,
    };
}
