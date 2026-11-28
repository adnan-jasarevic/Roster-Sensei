import { addDays, format } from 'date-fns';
import { type DaySlots, type MyAvailability, type TeamAvailability, type TeamAvailabilityMember } from '../types';

export const SLOT_MINUTES = 30;
export const SLOTS_PER_DAY = (24 * 60) / SLOT_MINUTES;
export const EMPTY_DAY: DaySlots = '0'.repeat(SLOTS_PER_DAY);
const SLOT_MS = SLOT_MINUTES * 60 * 1000;

/** The browser's IANA time zone, which is what a first save stores. */
export const browserTimeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';

/** "19:30" for slot 39. Slot 48 (the end of the last slot) is "24:00". */
export const slotTime = (slot: number) => {
    const minutes = slot * SLOT_MINUTES;
    return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
};

/** Monday = 0 ... Sunday = 6, the order the API sends the usual week in. */
export const weekdayIndex = (date: Date) => (date.getDay() + 6) % 7;

export const isFree = (day: DaySlots, slot: number) => day[slot] === '1';

/** The day with slots [from, to] (inclusive) set to free or not. */
export const paintDay = (day: DaySlots, from: number, to: number, free: boolean): DaySlots => {
    const chars = day.split('');
    for (let slot = Math.min(from, to); slot <= Math.max(from, to); slot++) chars[slot] = free ? '1' : '0';
    return chars.join('');
};

/** "yyyy-MM-dd" for today as a clock in that time zone shows it. */
export const todayIn = (timeZone: string) => {
    try {
        // en-CA formats dates as yyyy-MM-dd.
        return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
    } catch {
        return format(new Date(), 'yyyy-MM-dd');
    }
};

/** The seven "yyyy-MM-dd" dates of the week that starts on this Monday. */
export const weekDates = (monday: Date) => Array.from({ length: 7 }, (_, i) => format(addDays(monday, i), 'yyyy-MM-dd'));

/** What a date looks like for this user: its own override, or that weekday of the usual week. */
export const slotsOn = (availability: MyAvailability, date: string): DaySlots => {
    const override = availability.overrides.find(o => o.date === date);
    if (override) return override.slots;
    const [y, m, d] = date.split('-').map(Number);
    return availability.weekly[weekdayIndex(new Date(y, m - 1, d))] ?? EMPTY_DAY;
};

export const hasAnyFreeTime = (availability: MyAvailability) =>
    availability.weekly.some(day => day.includes('1')) || availability.overrides.some(o => o.slots.includes('1'));

/** The team's free people per half hour, by the slot's start time in milliseconds. */
export const freeBySlot = (team: TeamAvailability) => new Map(team.slots.map(s => [new Date(s.start).getTime(), s.userIds]));

/** The members who count towards a lineup: everyone but Coaches. */
export const lineupMemberIds = (team: TeamAvailability) => new Set(team.members.filter(m => m.countsTowardsLineup).map(m => m.userId));

export interface LineupWindow {
    start: Date;
    end: Date;
    /** The fewest players free at any point of the window, so the number that can play all of it. */
    playersFree: number;
    /** The players free for the whole window. */
    players: TeamAvailabilityMember[];
}

/**
 * Stretches of time when at least `playersNeeded` players (not Coaches) are free, with touching half
 * hours joined. Slots for which `skip` returns true (an off day, say) are left out.
 */
export function lineupWindows(team: TeamAvailability, skip?: (start: Date) => boolean): LineupWindow[] {
    const counting = lineupMemberIds(team);
    const byId = new Map(team.members.map(m => [m.userId, m]));
    const windows: { start: number; end: number; ids: Set<string> }[] = [];

    for (const slot of team.slots) {
        const start = new Date(slot.start).getTime();
        const ids = slot.userIds.filter(id => counting.has(id));
        if (ids.length < team.playersNeeded || skip?.(new Date(start))) continue;

        const last = windows[windows.length - 1];
        const together = last ? new Set(ids.filter(id => last.ids.has(id))) : null;
        // Joined only while the same five (or more) can play straight through; otherwise a new window starts.
        if (last && last.end === start && together && together.size >= team.playersNeeded) {
            last.end = start + SLOT_MS;
            last.ids = together;
        } else {
            windows.push({ start, end: start + SLOT_MS, ids: new Set(ids) });
        }
    }

    return windows.map(w => {
        const players = [...w.ids].map(id => byId.get(id)).filter((m): m is TeamAvailabilityMember => !!m);
        return { start: new Date(w.start), end: new Date(w.end), playersFree: players.length, players };
    });
}

/** "19:00–23:00" */
export const windowTimes = (window: { start: Date; end: Date }) => `${format(window.start, 'HH:mm')}–${format(window.end, 'HH:mm')}`;

/** "kenji, mika and yuki" */
export const listNames = (names: string[]) =>
    names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
