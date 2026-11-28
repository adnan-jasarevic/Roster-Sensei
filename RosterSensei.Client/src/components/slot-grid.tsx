import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import { cx } from '../lib/cx';
import { SLOTS_PER_DAY, slotTime } from '../lib/availability';
import './slot-grid.css';

export interface SlotGridColumn {
    key: string;
    /** What the column header shows. */
    header: ReactNode;
    /** The day in words, for screen readers: "Tuesday 24 November". */
    name: string;
    /** Past days: shown, but can't be painted. */
    disabled?: boolean;
}

export interface SlotCell {
    column: number;
    slot: number;
}

export interface SlotCellView {
    className?: string;
    /** Read after the day and time: "free", "6 of 7 players free". */
    description: string;
    content?: ReactNode;
}

export interface SlotGridProps {
    /** Names the grid for screen readers. */
    label: string;
    columns: SlotGridColumn[];
    /**
     * How one cell looks. `free` is set for a paintable grid: whether the cell shows as free right
     * now, including a drag that is still going on.
     */
    renderCell: (cell: SlotCell, free: boolean | undefined) => SlotCellView;
    /** Makes the grid paintable: drag (or Space) to mark times free, starting on a free time to clear them. */
    paint?: {
        isFree: (cell: SlotCell) => boolean;
        /** Called once per drag or key press, with every cell it covered. */
        onPaint: (cells: SlotCell[], free: boolean) => void;
    };
    /** The cell under the pointer or holding focus, for a details panel. Null when there is none. */
    onActiveChange?: (cell: SlotCell | null) => void;
    /** The half hour to scroll to first. */
    scrollToSlot?: number;
}

interface Drag {
    anchor: SlotCell;
    current: SlotCell;
    free: boolean;
    pointerId: number;
}

const cellFrom = (target: EventTarget | null): SlotCell | null => {
    const element = (target as HTMLElement | null)?.closest?.<HTMLElement>('[data-slot]');
    if (!element) return null;
    return { column: Number(element.dataset.column), slot: Number(element.dataset.slot) };
};

const inRect = (drag: Drag, { column, slot }: SlotCell) =>
    column >= Math.min(drag.anchor.column, drag.current.column) && column <= Math.max(drag.anchor.column, drag.current.column)
    && slot >= Math.min(drag.anchor.slot, drag.current.slot) && slot <= Math.max(drag.anchor.slot, drag.current.slot);

/**
 * A week of half hours: a column per day and a row per half hour, like When2Meet. One cell holds
 * focus at a time (arrow keys move it), so the 336 cells are a single Tab stop.
 */
export default function SlotGrid({ label, columns, renderCell, paint, onActiveChange, scrollToSlot = 32 }: SlotGridProps) {
    const scrollRef = useRef<HTMLDivElement>(null);
    const [focus, setFocus] = useState<SlotCell>(() => ({ column: Math.max(0, columns.findIndex(c => !c.disabled)), slot: scrollToSlot }));
    // The ref is the truth (pointerup and lostpointercapture both end a drag, and only one may commit it);
    // the state is what renders.
    const [drag, setDragState] = useState<Drag | null>(null);
    const dragRef = useRef<Drag | null>(null);
    const setDrag = (next: Drag | null) => {
        dragRef.current = next;
        setDragState(next);
    };
    const hovered = useRef<SlotCell | null>(null);
    const focused = useRef<SlotCell | null>(null);

    // Starts scrolled to the evening (or wherever asked), with an hour of context above it.
    useLayoutEffect(() => {
        const container = scrollRef.current;
        const row = container?.querySelector<HTMLElement>(`[data-slot="${Math.max(0, scrollToSlot - 2)}"]`);
        const header = container?.querySelector<HTMLElement>('[data-grid-header]');
        if (container && row) container.scrollTop = row.offsetTop - (header?.offsetHeight ?? 0);
        // Only on first render: later changes to scrollToSlot mustn't yank the grid while someone works in it.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const report = () => onActiveChange?.(hovered.current ?? focused.current);

    const moveFocus = (cell: SlotCell) => {
        const next = {
            column: Math.min(columns.length - 1, Math.max(0, cell.column)),
            slot: Math.min(SLOTS_PER_DAY - 1, Math.max(0, cell.slot)),
        };
        setFocus(next);
        scrollRef.current?.querySelector<HTMLElement>(`[data-column="${next.column}"][data-slot="${next.slot}"]`)?.focus();
    };

    const paintable = (cell: SlotCell) => !!paint && !columns[cell.column]?.disabled;

    const finishDrag = (commit: boolean) => {
        const drag = dragRef.current;
        if (!drag || !paint) return;
        setDrag(null);
        if (!commit) return;
        const cells: SlotCell[] = [];
        for (let column = Math.min(drag.anchor.column, drag.current.column); column <= Math.max(drag.anchor.column, drag.current.column); column++) {
            if (columns[column]?.disabled) continue;
            for (let slot = Math.min(drag.anchor.slot, drag.current.slot); slot <= Math.max(drag.anchor.slot, drag.current.slot); slot++) {
                cells.push({ column, slot });
            }
        }
        if (cells.length > 0) paint.onPaint(cells, drag.free);
    };

    const handlePointerDown = (event: PointerEvent<HTMLDivElement>) => {
        const cell = cellFrom(event.target);
        if (!cell) return;
        setFocus(cell);
        if (event.pointerType !== 'mouse') {
            // Touch has no hover, so a tap is what picks the cell for the details panel.
            hovered.current = cell;
            report();
        }
        if (!paint || !paintable(cell) || event.button !== 0) return;
        event.preventDefault();
        (event.target as HTMLElement).focus({ preventScroll: true });
        event.currentTarget.setPointerCapture(event.pointerId);
        // Starting on a free time clears; starting on a busy one fills. The whole drag does the same.
        setDrag({ anchor: cell, current: cell, free: !paint.isFree(cell), pointerId: event.pointerId });
    };

    const handlePointerMove = (event: PointerEvent<HTMLDivElement>) => {
        // While the pointer is captured, the event target stays the first cell, so look up what is under it.
        const drag = dragRef.current;
        const cell = drag
            ? cellFrom(document.elementFromPoint(event.clientX, event.clientY))
            : cellFrom(event.target);
        if (drag && cell && event.pointerId === drag.pointerId && (cell.column !== drag.current.column || cell.slot !== drag.current.slot)) {
            setDrag({ ...drag, current: cell });
        }
        if (event.pointerType === 'mouse' && (cell?.column !== hovered.current?.column || cell?.slot !== hovered.current?.slot)) {
            hovered.current = cell;
            report();
        }
    };

    const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
        const { column, slot } = focus;
        const moves: Record<string, SlotCell> = {
            ArrowUp: { column, slot: slot - 1 },
            ArrowDown: { column, slot: slot + 1 },
            ArrowLeft: { column: column - 1, slot },
            ArrowRight: { column: column + 1, slot },
            // Two hours at a time.
            PageUp: { column, slot: slot - 4 },
            PageDown: { column, slot: slot + 4 },
            Home: { column: event.ctrlKey ? column : 0, slot: event.ctrlKey ? 0 : slot },
            End: { column: event.ctrlKey ? column : columns.length - 1, slot: event.ctrlKey ? SLOTS_PER_DAY - 1 : slot },
        };
        const move = moves[event.key];
        if (move) {
            event.preventDefault();
            moveFocus(move);
            return;
        }
        if ((event.key === ' ' || event.key === 'Enter') && paint && paintable(focus)) {
            event.preventDefault();
            paint.onPaint([focus], !paint.isFree(focus));
        }
    };

    return (
        <div
            ref={scrollRef}
            className="rs-slot-grid-scroll max-h-[min(62dvh,40rem)] overflow-y-auto overscroll-contain rounded-md border border-line bg-surface"
        >
            <div
                role="grid"
                aria-label={label}
                aria-rowcount={SLOTS_PER_DAY + 1}
                aria-colcount={columns.length + 1}
                className={cx('rs-slot-grid', paint && 'is-paintable', drag && 'is-dragging')}
                style={{ gridTemplateColumns: `3.25rem repeat(${columns.length}, minmax(0, 1fr))` }}
                onPointerDown={handlePointerDown}
                onPointerMove={handlePointerMove}
                onPointerUp={() => finishDrag(true)}
                onPointerCancel={() => finishDrag(false)}
                onLostPointerCapture={() => finishDrag(true)}
                onPointerLeave={() => {
                    hovered.current = null;
                    report();
                }}
                onKeyDown={handleKeyDown}
                onFocus={event => {
                    focused.current = cellFrom(event.target);
                    report();
                }}
                onBlur={event => {
                    if (event.currentTarget.contains(event.relatedTarget as Node | null)) return;
                    focused.current = null;
                    report();
                }}
            >
                <div role="row" aria-rowindex={1} className="contents" data-grid-header>
                    <div role="columnheader" className="rs-slot-grid-corner">
                        <span className="sr-only">Time</span>
                    </div>
                    {columns.map((column, index) => (
                        <div key={column.key} role="columnheader" aria-colindex={index + 2} className={cx('rs-slot-grid-day', column.disabled && 'is-disabled')}>
                            {column.header}
                        </div>
                    ))}
                </div>

                {Array.from({ length: SLOTS_PER_DAY }, (_, slot) => (
                    <div key={slot} role="row" aria-rowindex={slot + 2} className="contents">
                        <div role="rowheader" className={cx('rs-slot-grid-time', slot % 2 === 0 && 'is-hour')}>
                            {/* Only the hours are printed; screen readers get every half hour. */}
                            <span aria-hidden="true">{slot % 2 === 0 ? slotTime(slot) : ''}</span>
                            <span className="sr-only">{slotTime(slot)} to {slotTime(slot + 1)}</span>
                        </div>
                        {columns.map((column, index) => {
                            const cell = { column: index, slot };
                            const free = paint ? (drag && !column.disabled && inRect(drag, cell) ? drag.free : paint.isFree(cell)) : undefined;
                            const view = renderCell(cell, free);
                            const isFocus = focus.column === index && focus.slot === slot;
                            return (
                                <div
                                    key={column.key}
                                    role="gridcell"
                                    aria-colindex={index + 2}
                                    aria-label={`${column.name}, ${slotTime(slot)}: ${view.description}`}
                                    aria-selected={paint ? free : undefined}
                                    aria-readonly={paint ? column.disabled || undefined : undefined}
                                    tabIndex={isFocus ? 0 : -1}
                                    data-column={index}
                                    data-slot={slot}
                                    className={cx('rs-slot', slot % 2 === 0 && 'is-hour', column.disabled && 'is-disabled', view.className)}
                                >
                                    {view.content}
                                </div>
                            );
                        })}
                    </div>
                ))}
            </div>
        </div>
    );
}
