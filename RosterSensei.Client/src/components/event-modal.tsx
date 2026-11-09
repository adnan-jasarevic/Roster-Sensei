import { useEffect, useState } from 'react';
import axios from 'axios';
import { format } from 'date-fns';
import { CalendarOff, Trash2 } from 'lucide-react';
import api from '../services/api';
import { getApiErrorMessage } from '../services/errors';
import { eventTitle, otherTypeOf, pickableTypes } from '../lib/event-types';
import type { SaveTeamEventRequest, TeamEvent, TeamEventType } from '../types';
import { Alert, Button, ConfirmDialog, Field, Input, Modal, SegmentedControl, Textarea } from './ui';

interface EventModalProps {
    teamId: number;
    // An existing event to view or edit; null to create a new one.
    event: TeamEvent | null;
    // Prefilled times when creating from a calendar selection.
    initialStart?: Date;
    initialEnd?: Date;
    // The team's event types, "Other" last.
    types: TeamEventType[];
    // True when the event sits on an off day, which makes it cancelled.
    onOffDay?: boolean;
    canManage: boolean;
    onClose: () => void;
    onSaved: () => void;
    // Offered instead of a new event: marks the day of the selected start as an off day.
    onMarkOffDay?: (day: Date) => void;
}

// datetime-local wants "yyyy-MM-ddTHH:mm" in the user's local time.
const toInputValue = (date: Date) => format(date, "yyyy-MM-dd'T'HH:mm");

// The type a new event starts with: Pracc when the team has it, otherwise the first type on the list.
const defaultTypeId = (types: TeamEventType[]) =>
    (types.find(t => t.preset === 'Pracc') ?? pickableTypes(types)[0] ?? otherTypeOf(types))?.id ?? 0;

export default function EventModal({ teamId, event, initialStart, initialEnd, types, onOffDay = false, canManage, onClose, onSaved, onMarkOffDay }: EventModalProps) {
    const [title, setTitle] = useState(event?.title ?? '');
    const [description, setDescription] = useState(event?.description ?? '');
    // An event whose type was removed a moment ago is under Other by now.
    const [eventTypeId, setEventTypeId] = useState(() =>
        event && types.some(t => t.id === event.eventTypeId) ? event.eventTypeId : event ? otherTypeOf(types)?.id ?? 0 : defaultTypeId(types),
    );
    const [start, setStart] = useState(() => toInputValue(event ? new Date(event.startTime) : initialStart ?? new Date()));
    const [end, setEnd] = useState(() => {
        if (event) return toInputValue(new Date(event.endTime));
        const defaultStart = initialStart ?? new Date();
        return toInputValue(initialEnd ?? new Date(defaultStart.getTime() + 2 * 60 * 60 * 1000));
    });
    const [error, setError] = useState('');
    const [showFieldErrors, setShowFieldErrors] = useState(false);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [isDeleting, setIsDeleting] = useState(false);
    const [isConfirmingDelete, setIsConfirmingDelete] = useState(false);
    const [deleteError, setDeleteError] = useState('');

    const readOnly = !canManage;
    const selectedType = types.find(t => t.id === eventTypeId);
    const startError = showFieldErrors && !start ? 'Pick a start time.' : undefined;
    const endError = showFieldErrors && !end
        ? 'Pick an end time.'
        : start && end && new Date(end) <= new Date(start) ? 'The end has to be after the start.' : undefined;

    const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
        e.preventDefault();
        if (readOnly) return;
        setError('');

        if (!start || !end || endError) {
            setShowFieldErrors(true);
            return;
        }

        const payload: SaveTeamEventRequest = {
            // An empty title is fine: the calendar shows the type's name instead.
            title: title.trim(),
            description: description.trim() || undefined,
            // new Date() reads the datetime-local value as local time; toISOString() sends UTC.
            startTime: new Date(start).toISOString(),
            endTime: new Date(end).toISOString(),
            eventTypeId,
        };

        setIsSubmitting(true);
        try {
            if (event) await api.put(`/teams/${teamId}/events/${event.id}`, { ...payload, version: event.version });
            else await api.post(`/teams/${teamId}/events`, payload);
            onSaved();
            onClose();
        } catch (err: unknown) {
            setError(getApiErrorMessage(err, 'Failed to save the event.'));
            // Someone else saved a newer version, or deleted the event: refresh the calendar behind the dialog
            // so reopening the event shows what is there now. The dialog stays open so nothing typed is lost.
            const status = axios.isAxiosError(err) ? err.response?.status : undefined;
            if (status === 409 || status === 404) onSaved();
        } finally {
            setIsSubmitting(false);
        }
    };

    const handleDelete = async () => {
        if (!event) return;
        setDeleteError('');
        setIsDeleting(true);
        try {
            await api.delete(`/teams/${teamId}/events/${event.id}`);
            onSaved();
            onClose();
        } catch (err: unknown) {
            setDeleteError(getApiErrorMessage(err, 'Failed to delete the event.'));
            setIsDeleting(false);
        }
    };

    // Times the event would take, or null while they're incomplete or the wrong way round.
    const range = start && end && !endError ? { from: new Date(start), to: new Date(end) } : null;
    const overlaps = useOverlappingEvents(readOnly ? null : teamId, range?.from.getTime(), range?.to.getTime(), event?.id);

    const heading = readOnly ? 'Event' : event ? 'Edit event' : 'New event';

    const typeOptions = types
        .filter(t => t.preset !== 'RecordedMatch' || t.id === eventTypeId)
        .map(t => ({
            value: t.id,
            label: (
                <span className="flex items-center gap-1.5">
                    <span className="size-2.5 shrink-0 rounded-full ring-1 ring-line-strong" style={{ backgroundColor: t.color }} aria-hidden="true" />
                    {t.name}
                </span>
            ),
        }));

    return (
        <Modal
            open
            onClose={onClose}
            title={heading}
            busy={isSubmitting || isDeleting}
            closeOnBackdrop={readOnly}
            footer={readOnly ? (
                <Button variant="secondary" onClick={onClose}>Close</Button>
            ) : (
                <>
                    {!event && onMarkOffDay && (
                        <Button
                            variant="ghost"
                            className="mr-auto"
                            onClick={() => onMarkOffDay(new Date(start || Date.now()))}
                            disabled={isSubmitting}
                            leftIcon={<CalendarOff className="size-4" aria-hidden="true" />}
                        >
                            Make it an off day
                        </Button>
                    )}
                    {event && (
                        <Button
                            variant="ghost"
                            className="mr-auto text-danger hover:text-danger"
                            onClick={() => { setDeleteError(''); setIsConfirmingDelete(true); }}
                            disabled={isSubmitting}
                            leftIcon={<Trash2 className="size-4" aria-hidden="true" />}
                        >
                            Delete
                        </Button>
                    )}
                    <Button variant="ghost" onClick={onClose} disabled={isSubmitting}>Cancel</Button>
                    <Button type="submit" form="event-form" loading={isSubmitting} loadingText="Saving...">{event ? 'Save' : 'Add event'}</Button>
                </>
            )}
        >
            <form id="event-form" onSubmit={handleSubmit} noValidate className="flex flex-col gap-5">
                {error && <Alert tone="danger">{error}</Alert>}
                {onOffDay && (
                    <Alert tone="warning">
                        This day is an off day, so the event is cancelled. It comes back if the day stops being an off day.
                    </Alert>
                )}

                <Field label="Title" optional={!readOnly} hint={readOnly ? undefined : `Leave it empty to use the type's name (${selectedType?.name ?? 'Event'}).`}>
                    <Input
                        value={readOnly ? eventTitle(title, selectedType) : title}
                        onChange={(e) => setTitle(e.target.value)}
                        placeholder={selectedType?.name ?? 'e.g. Pracc vs Tokyo Ronin'}
                        maxLength={100}
                        disabled={readOnly}
                        autoComplete="off"
                        data-autofocus={readOnly ? undefined : true}
                    />
                </Field>

                <SegmentedControl label="Type" value={eventTypeId} options={typeOptions} onChange={setEventTypeId} disabled={readOnly} />

                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <Field label="Starts" required={!readOnly} error={startError}>
                        <Input type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} disabled={readOnly} />
                    </Field>
                    <Field label="Ends" required={!readOnly} error={endError}>
                        <Input type="datetime-local" value={end} onChange={(e) => setEnd(e.target.value)} disabled={readOnly} />
                    </Field>
                </div>

                {overlaps.length > 0 && (
                    <Alert tone="warning">
                        <span data-testid="overlap-warning">
                            {overlaps.length === 1 ? 'This overlaps ' : `This overlaps ${overlaps.length} events: `}
                            {overlaps.slice(0, 3).map((other, index) => (
                                <span key={other.id}>
                                    {index > 0 && ', '}
                                    <strong className="text-fg">{eventTitle(other.title, types.find(t => t.id === other.eventTypeId))}</strong>
                                    {' '}({formatTimes(other)})
                                </span>
                            ))}
                            {overlaps.length > 3 && ` and ${overlaps.length - 3} more`}. You can still save it.
                        </span>
                    </Alert>
                )}

                <Field label="Description" optional={!readOnly}>
                    <Textarea value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} disabled={readOnly} />
                </Field>
            </form>

            <ConfirmDialog
                open={isConfirmingDelete}
                onClose={() => setIsConfirmingDelete(false)}
                onConfirm={handleDelete}
                title="Delete event"
                confirmLabel="Delete event"
                busyLabel="Deleting..."
                busy={isDeleting}
                error={deleteError}
            >
                Delete <strong className="text-fg">{eventTitle(event?.title, selectedType)}</strong>? This can't be undone.
            </ConfirmDialog>
        </Modal>
    );
}

const formatTimes = (other: TeamEvent) => {
    const from = new Date(other.startTime);
    const to = new Date(other.endTime);
    const sameDay = format(from, 'yyyy-MM-dd') === format(to, 'yyyy-MM-dd');
    return `${format(from, 'EEE d MMM, HH:mm')}–${format(to, sameDay ? 'HH:mm' : 'EEE d MMM, HH:mm')}`;
};

// Waits this long after the last change to the times before asking the server.
const OVERLAP_CHECK_DELAY_MS = 300;

/**
 * The team's other events that overlap the given times, asked from the server so events outside the
 * calendar's visible range count too. Only a reminder: overlapping events can still be saved. A check
 * still waiting or in flight when the times change again is cancelled, so an older answer can't win.
 */
function useOverlappingEvents(teamId: number | null, fromMs: number | undefined, toMs: number | undefined, ownId: number | undefined) {
    const [result, setResult] = useState<{ key: string; events: TeamEvent[] } | null>(null);
    const key = teamId !== null && fromMs !== undefined && toMs !== undefined && !Number.isNaN(fromMs) && !Number.isNaN(toMs)
        ? `${teamId}:${fromMs}:${toMs}`
        : null;

    useEffect(() => {
        if (key === null) return;
        const controller = new AbortController();
        const timer = window.setTimeout(() => {
            api.get<TeamEvent[]>(`/teams/${teamId}/events`, {
                signal: controller.signal,
                params: { from: new Date(fromMs!).toISOString(), to: new Date(toMs!).toISOString() },
            }).then(
                response => setResult({ key, events: response.data }),
                // Only a reminder: if the check fails, saving still works and the server has the last word.
                () => undefined,
            );
        }, OVERLAP_CHECK_DELAY_MS);
        return () => {
            window.clearTimeout(timer);
            controller.abort();
        };
    }, [key, teamId, fromMs, toMs]);

    // An answer for times the form no longer has is ignored.
    if (key === null || result?.key !== key) return [];
    return result.events.filter(other => other.id !== ownId);
}
