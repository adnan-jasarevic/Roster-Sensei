import type { Page } from '@playwright/test';
import { test, expect } from './support/fixtures';
import { teamEvent } from './support/mock-api';

// Wednesday 11 November 2026, 10:00 in Sarajevo (UTC+1). That week runs Monday 9 to Sunday 15.
const NOW = new Date('2026-11-11T09:00:00Z');

async function openCalendar(page: Page, view?: 'Week') {
    await page.clock.setFixedTime(NOW);
    await page.goto('/calendar');
    if (view) await page.getByRole('button', { name: view, exact: true }).click();
    await expect(page.locator('.rbc-calendar')).toBeVisible();
}

/** The week view's day column for the given day of the week, Monday = 0. */
const dayColumn = (page: Page, index: number) => page.locator('.rbc-time-content .rbc-day-slot').nth(index);

/** Drags the element to the same height in another day column. */
async function dragToDay(page: Page, source: ReturnType<Page['locator']>, targetDay: number) {
    const from = await source.boundingBox();
    const column = await dayColumn(page, targetDay).boundingBox();
    if (!from || !column) throw new Error('Calendar not laid out');
    const x = from.x + from.width / 2;
    const y = from.y + 10;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + 5, y + 5, { steps: 2 });
    await page.mouse.move(column.x + column.width / 2, y, { steps: 10 });
    await page.mouse.up();
}

test.describe('calendar', () => {
    test('the week view shows each day name under its date', async ({ page, api }) => {
        await api.install(page);
        await openCalendar(page, 'Week');

        const monday = page.locator('.rbc-time-header .rbc-header').first();
        const date = monday.locator('.rs-day-header-date');
        const name = monday.locator('.rs-day-header-name');
        await expect(date.getByText('9 Nov', { exact: true })).toBeVisible();
        // Spelled out on wide screens; phones get the short name.
        await expect(name.getByText('Monday', { exact: true })).toBeVisible();
        await expect(name.getByText('Mon', { exact: true })).toBeHidden();

        const dateBox = await date.boundingBox();
        const nameBox = await name.boundingBox();
        expect(nameBox!.y).toBeGreaterThanOrEqual(dateBox!.y + dateBox!.height - 1);
    });

    test('an event without a title is saved and shown under its type name', async ({ page, api }) => {
        await api.install(page);
        await openCalendar(page);

        await page.getByRole('button', { name: 'Add event' }).click();
        const dialog = page.getByRole('dialog', { name: 'New event' });
        await expect(dialog.getByLabel(/^Title/)).toHaveAttribute('placeholder', 'Pracc');
        await dialog.getByRole('button', { name: 'Add event' }).click();
        await expect(dialog).toBeHidden();

        const [created] = api.requestsTo('POST', /^\/teams\/1\/events$/);
        expect(created.body).toMatchObject({ title: '', eventTypeId: 1 });
        await expect(page.locator('.rbc-event').filter({ hasText: 'Pracc' })).toBeVisible();
    });

    test('creating an overlapping event shows a reminder but still saves', async ({ page, api }) => {
        // 11:30-13:30 local; the new event defaults to 11:00-13:00.
        api.teams[0].events = [teamEvent(7, 'Aim session', '2026-11-11T10:30:00Z', '2026-11-11T12:30:00Z')];
        await api.install(page);
        await openCalendar(page);

        await page.getByRole('button', { name: 'Add event' }).click();
        const dialog = page.getByRole('dialog', { name: 'New event' });
        const warning = dialog.getByTestId('overlap-warning');
        await expect(warning).toContainText('This overlaps Aim session (Wed 11 Nov, 11:30–13:30)');

        // Moving it clear of the other event takes the reminder away.
        await dialog.getByLabel(/^Starts/).fill('2026-11-11T14:00');
        await dialog.getByLabel(/^Ends/).fill('2026-11-11T15:00');
        await expect(warning).toBeHidden();

        await dialog.getByLabel(/^Starts/).fill('2026-11-11T12:00');
        await expect(warning).toBeVisible();
        await dialog.getByRole('button', { name: 'Add event' }).click();
        await expect(dialog).toBeHidden();
        expect(api.requestsTo('POST', /^\/teams\/1\/events$/)).toHaveLength(1);
    });

    test('dragging an event moves it to the new day, sending the version it was shown with', async ({ page, api }) => {
        // Wednesday 13:00-15:00 local.
        api.teams[0].events = [teamEvent(7, 'Aim session', '2026-11-11T12:00:00Z', '2026-11-11T14:00:00Z', { version: 4 })];
        await api.install(page);
        await openCalendar(page, 'Week');

        const event = page.locator('.rbc-event').filter({ hasText: 'Aim session' });
        await expect(dayColumn(page, 2).locator('.rbc-event')).toHaveCount(1);
        await dragToDay(page, event, 3);

        await expect(page.getByText(/^Moved Aim session to Thu 12 Nov, 13:00/)).toBeVisible();
        const [put] = api.requestsTo('PUT', /^\/teams\/1\/events\/7$/);
        expect(put.body).toMatchObject({
            title: 'Aim session',
            startTime: '2026-11-12T12:00:00.000Z',
            endTime: '2026-11-12T14:00:00.000Z',
            version: 4,
        });
        await expect(dayColumn(page, 3).locator('.rbc-event')).toHaveCount(1);
        await expect(dayColumn(page, 2).locator('.rbc-event')).toHaveCount(0);
    });

    test('a drag over someone else\'s newer edit is refused and the event goes back', async ({ page, api }) => {
        api.teams[0].events = [teamEvent(7, 'Aim session', '2026-11-11T12:00:00Z', '2026-11-11T14:00:00Z', { version: 2 })];
        await api.install(page);
        await openCalendar(page, 'Week');
        // Someone else saved it after the calendar loaded.
        api.teams[0].events[0].version = 3;

        await dragToDay(page, page.locator('.rbc-event').filter({ hasText: 'Aim session' }), 3);

        await expect(page.getByText('Someone else changed this event while you were editing it.', { exact: false })).toBeVisible();
        await expect(dayColumn(page, 2).locator('.rbc-event')).toHaveCount(1);
        await expect(dayColumn(page, 3).locator('.rbc-event')).toHaveCount(0);
    });

    test('an event cannot be dragged onto an off day', async ({ page, api }) => {
        api.teams[0].events = [teamEvent(7, 'Aim session', '2026-11-11T12:00:00Z', '2026-11-11T14:00:00Z')];
        api.teams[0].offDays = [{ date: '2026-11-12', startTime: '2026-11-11T23:00:00Z', endTime: '2026-11-12T23:00:00Z' }];
        await api.install(page);
        await openCalendar(page, 'Week');

        await dragToDay(page, page.locator('.rbc-event').filter({ hasText: 'Aim session' }), 3);

        await expect(page.getByText('Thursday 12 November is an off day, so no events can be moved onto it.')).toBeVisible();
        expect(api.requestsTo('PUT', /^\/teams\/1\/events\/7$/)).toHaveLength(0);
        await expect(dayColumn(page, 2).locator('.rbc-event')).toHaveCount(1);
    });

    test('a Player cannot drag events', async ({ page, api }) => {
        api.teams[0].role = 'Player';
        api.teams[0].isOwner = false;
        api.teams[0].events = [teamEvent(7, 'Aim session', '2026-11-11T12:00:00Z', '2026-11-11T14:00:00Z')];
        await api.install(page);
        await openCalendar(page, 'Week');

        await dragToDay(page, page.locator('.rbc-event').filter({ hasText: 'Aim session' }), 3);

        expect(api.requestsTo('PUT', /^\/teams\/1\/events\/7$/)).toHaveLength(0);
        await expect(dayColumn(page, 2).locator('.rbc-event')).toHaveCount(1);
    });
});
