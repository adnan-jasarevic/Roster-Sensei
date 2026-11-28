import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { test, expect } from './support/fixtures';
import { ME, freeDay, freeSlots, member, team, type MockApi } from './support/mock-api';

// Wednesday 11 November 2026, 10:00 in Sarajevo (UTC+1). That week runs Monday 9 to Sunday 15.
const NOW = new Date('2026-11-11T09:00:00Z');
const EMPTY = '0'.repeat(48);

/** Six players and a coach. Five of the players are free on Tuesday 18:00-20:00 Sarajevo time. */
function rosterWithFreeTime(api: MockApi) {
    const t = team(1, 'Tokyo Ronin');
    t.members.push(member('yuki', 'Player'), member('ren', 'Player'), member('hiro', 'Coach'));
    const five = [ME.id, 'kenji', 'mika', 'yuki', 'ren'];
    t.availability = [
        ...freeSlots('2026-11-10T16:00:00Z', '2026-11-10T17:00:00Z', ['kenji', 'mika']),
        ...freeSlots('2026-11-10T17:00:00Z', '2026-11-10T19:00:00Z', [...five, 'hiro']),
        // Four players and the coach: not a lineup, because coaches don't count.
        ...freeSlots('2026-11-12T18:00:00Z', '2026-11-12T19:00:00Z', ['kenji', 'mika', 'yuki', 'ren', 'hiro']),
    ];
    api.teams = [t];
    api.myAvailability = { timeZone: 'Europe/Sarajevo', weekly: [freeDay('19:00', '21:00'), EMPTY, EMPTY, EMPTY, EMPTY, EMPTY, EMPTY], overrides: [] };
}

async function openAvailability(page: Page) {
    await page.clock.setFixedTime(NOW);
    await page.goto('/availability');
    await expect(page.getByRole('heading', { name: 'Availability (When2Play)', level: 1 })).toBeVisible();
    await expect(page.getByRole('grid')).toBeVisible();
}

const cell = (page: Page, day: string, time: string) => page.getByRole('gridcell', { name: new RegExp(`^${day}, ${time}:`) });

/** Drags with the mouse from the middle of one cell to the middle of another. */
async function paint(page: Page, from: ReturnType<typeof cell>, to: ReturnType<typeof cell>) {
    await from.scrollIntoViewIfNeeded();
    const a = await from.boundingBox();
    const b = await to.boundingBox();
    if (!a || !b) throw new Error('Grid not laid out');
    await page.mouse.move(a.x + a.width / 2, a.y + a.height / 2);
    await page.mouse.down();
    await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2, { steps: 8 });
    await page.mouse.up();
}

test.describe('availability', () => {
    test('someone who has filled nothing in starts on their own usual week', async ({ page, api }) => {
        await api.install(page);
        await openAvailability(page);

        await expect(page.getByRole('radio', { name: 'My availability' })).toBeChecked();
        await expect(page.getByRole('radio', { name: 'Usual week' })).toBeChecked();
        await expect(page.getByRole('grid', { name: 'Your usual week' })).toBeVisible();
    });

    test('dragging over the usual week paints a block and saves the whole week', async ({ page, api }) => {
        await api.install(page);
        await openAvailability(page);

        // Monday 19:00 to Tuesday 20:30: both days, 19:00 to 21:00.
        await paint(page, cell(page, 'Every Monday', '19:00'), cell(page, 'Every Tuesday', '20:30'));

        await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
        const saves = api.requestsTo('PUT', /^\/availability\/weekly$/);
        expect(saves).toHaveLength(1);
        const body = saves[0].body as { timeZone: string; weekly: string[] };
        expect(body.timeZone).toBe('Europe/Sarajevo');
        expect(body.weekly).toEqual([freeDay('19:00', '21:00'), freeDay('19:00', '21:00'), EMPTY, EMPTY, EMPTY, EMPTY, EMPTY]);
        await expect(cell(page, 'Every Monday', '20:30')).toHaveAttribute('aria-selected', 'true');
        await expect(cell(page, 'Every Monday', '21:00')).toHaveAttribute('aria-selected', 'false');
    });

    test('a drag that starts on a free time clears instead', async ({ page, api }) => {
        rosterWithFreeTime(api);
        await api.install(page);
        await openAvailability(page);
        await page.getByText('My availability', { exact: true }).click();

        await paint(page, cell(page, 'Every Monday', '20:00'), cell(page, 'Every Monday', '20:30'));

        await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
        const [save] = api.requestsTo('PUT', /^\/availability\/weekly$/);
        expect((save.body as { weekly: string[] }).weekly[0]).toBe(freeDay('19:00', '20:00'));
    });

    test('the keyboard moves through the grid and Space switches a half hour', async ({ page, api }) => {
        await api.install(page);
        await openAvailability(page);

        await cell(page, 'Every Wednesday', '18:00').click();
        await page.keyboard.press('ArrowDown');
        await expect(cell(page, 'Every Wednesday', '18:30')).toBeFocused();
        await page.keyboard.press('Space');

        await expect(cell(page, 'Every Wednesday', '18:30')).toHaveAttribute('aria-selected', 'true');
        await expect.poll(() => api.requestsTo('PUT', /^\/availability\/weekly$/).length).toBe(2);
        // The click painted 18:00, then Space painted 18:30.
        const last = api.requestsTo('PUT', /^\/availability\/weekly$/).at(-1)!.body as { weekly: string[] };
        expect(last.weekly[2]).toBe(freeDay('18:00', '19:00'));
    });

    test('a specific week changes only that date, and Reset brings the usual week back', async ({ page, api }) => {
        rosterWithFreeTime(api);
        await api.install(page);
        await openAvailability(page);
        await page.getByText('My availability', { exact: true }).click();
        await page.getByText('Specific week', { exact: true }).click();

        // Past days can't be changed.
        await expect(cell(page, 'Monday 9 November', '19:00')).toHaveAttribute('aria-readonly', 'true');

        await paint(page, cell(page, 'Friday 13 November', '20:00'), cell(page, 'Friday 13 November', '20:30'));
        await expect(page.getByRole('status').filter({ hasText: 'Saved' })).toBeVisible();
        const [save] = api.requestsTo('PUT', /^\/availability\/days\/2026-11-13$/);
        expect(save.body).toEqual({ timeZone: 'Europe/Sarajevo', slots: freeDay('20:00', '21:00') });
        expect(api.requestsTo('PUT', /^\/availability\/weekly$/)).toHaveLength(0);

        await page.getByRole('button', { name: /^Friday 13 November differs from your usual week/ }).click();
        await expect.poll(() => api.requestsTo('DELETE', /^\/availability\/days\/2026-11-13$/).length).toBe(1);
        await expect(cell(page, 'Friday 13 November', '20:00')).toHaveAttribute('aria-selected', 'false');
        await expect(page.getByRole('button', { name: /differs from your usual week/ })).toHaveCount(0);
    });

    test('a failed save says so and puts back what the server has', async ({ page, api }) => {
        await api.install(page);
        api.fail('PUT', /^\/availability\/weekly$/, 500, 'The server is having a moment.');
        await openAvailability(page);

        await paint(page, cell(page, 'Every Monday', '19:00'), cell(page, 'Every Monday', '19:30'));

        await expect(page.getByRole('alert').filter({ hasText: 'The server is having a moment.' })).toBeVisible();
        await expect(cell(page, 'Every Monday', '19:00')).toHaveAttribute('aria-selected', 'false');
    });

    test('the team view counts players, not coaches, and lists lineup windows', async ({ page, api }) => {
        rosterWithFreeTime(api);
        await api.install(page);
        await openAvailability(page);

        await expect(page.getByRole('radio', { name: 'Team' })).toBeChecked();
        const details = page.getByRole('complementary', { name: 'Details' });
        // Only Tuesday: Thursday has four players and a coach.
        await expect(details.getByRole('listitem')).toHaveCount(1);
        await expect(details.getByRole('listitem')).toContainText('Tue 10 Nov');
        await expect(details.getByRole('listitem')).toContainText('18:00–20:00');

        await expect(cell(page, 'Thursday 12 November', '19:00')).toHaveAttribute('aria-label', /4 of 5 players free, plus a coach$/);

        await cell(page, 'Tuesday 10 November', '18:30').hover();
        await expect(details.getByText('Lineup ready')).toBeVisible();
        await expect(details.getByText('hiro: free')).toBeVisible();
    });

    test('a team without five players explains why nothing is marked', async ({ page, api }) => {
        await api.install(page);
        api.myAvailability.weekly[0] = freeDay('19:00', '20:00');
        await openAvailability(page);

        await expect(page.getByText(/A lineup needs 5 players and Tokyo Ronin has 3\./)).toBeVisible();
    });

    test('the calendar shows free time for a lineup, and a manager can plan in it', async ({ page, api }) => {
        rosterWithFreeTime(api);
        await api.install(page);
        await page.clock.setFixedTime(NOW);
        await page.goto('/calendar');
        await expect(page.locator('.rbc-calendar')).toBeVisible();

        const tag = page.getByRole('button', { name: /^18:00–20:00, 5 players free/ });
        await expect(tag).toBeVisible();
        // Thursday's four players and a coach aren't enough.
        await expect(page.locator('.rs-lineup-tag')).toHaveCount(1);

        await tag.click();
        const dialog = page.getByRole('dialog', { name: 'New event' });
        await expect(dialog).toBeVisible();
        await dialog.getByRole('button', { name: 'Cancel' }).click();

        await page.getByRole('button', { name: 'Week', exact: true }).click();
        const band = page.locator('.rbc-background-event.is-lineup-window');
        await expect(band).toHaveCount(1);
        await expect(band).toContainText('5 free');
        // Scenery, not a button: clicks go through it to the time slots.
        await expect(band).not.toHaveAttribute('role', 'button');
        await expect(band).not.toHaveAttribute('tabindex');
    });

    test('no free time is shown on an off day', async ({ page, api }) => {
        rosterWithFreeTime(api);
        api.teams[0].offDays = [{ date: '2026-11-10', startTime: '2026-11-09T23:00:00Z', endTime: '2026-11-10T23:00:00Z' }];
        await api.install(page);
        await page.clock.setFixedTime(NOW);
        await page.goto('/calendar');
        await expect(page.locator('.rbc-calendar')).toBeVisible();

        await expect(page.getByText('Off day', { exact: true }).first()).toBeVisible();
        await expect(page.locator('.rs-lineup-tag')).toHaveCount(0);
    });

    for (const theme of ['dark', 'light'] as const) {
        test(`the painting grid passes an accessibility scan, ${theme} theme`, async ({ page, api }) => {
            rosterWithFreeTime(api);
            await page.addInitScript(t => localStorage.setItem('rs-theme', t), theme);
            await api.install(page);
            await openAvailability(page);
            await page.getByText('My availability', { exact: true }).click();
            await page.getByText('Specific week', { exact: true }).click();
            await expect(page.getByRole('grid', { name: /^Your availability/ })).toBeVisible();
            // Let the segmented control's colour transition finish, as the page sweep does.
            await page.waitForTimeout(400);

            const results = await new AxeBuilder({ page })
                .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
                .analyze();
            expect(results.violations.map(v => `${v.id}: ${v.help} (${v.nodes.map(n => n.target.join(' ')).join(', ')})`)).toEqual([]);
        });
    }
});
