import type { Locator, Page } from '@playwright/test';
import { test, expect, focused } from './support/fixtures';
import { ME, MAP_POOL_MAPS, team, type MockApi } from './support/mock-api';
import type { MapRating, MapSide } from '../src/types';

const rating = (userId: string, side: MapSide, mapName: string, value: number): MapRating => ({ userId, side, mapName, rating: value });

function withRatings(api: MockApi, ratings: MapRating[] = []) {
    const t = team(1, 'Tokyo Ronin', 'Player', false, { mapRatings: ratings });
    api.teams = [t];
    return t;
}

const sideTable = (page: Page, side: MapSide) => page.getByRole('table', { name: new RegExp(`^${side} side ratings`) });

/** The map names of a table's rows, top to bottom. */
const mapOrder = (table: Locator) => table.locator('tbody th[scope="row"]');

/** A row's cells after the map name: one per player (sensei, boss, kenji, mika), then the result. */
const rowCells = (table: Locator, map: string) => table.getByRole('row').filter({ has: table.page().getByRole('rowheader', { name: map, exact: true }) }).getByRole('cell');

async function open(page: Page, api: MockApi) {
    await api.install(page);
    await page.goto('/map-pool');
    await expect(page.getByRole('heading', { name: 'Map Pool', level: 1 })).toBeVisible();
    await expect(sideTable(page, 'T')).toBeVisible();
}

test.describe('Map Pool', () => {
    test('shows a T and a CT table with a column per member and the average as the result', async ({ page, api }) => {
        withRatings(api, [
            rating('kenji', 'T', 'Nuke', 6),
            rating('mika', 'T', 'Nuke', 1),
            rating('boss', 'T', 'Nuke', 3.5),
            rating('kenji', 'CT', 'Nuke', 9),
        ]);
        await open(page, api);

        for (const side of ['T', 'CT'] as const) {
            const table = sideTable(page, side);
            await expect(table.getByRole('columnheader')).toHaveText([/^Map/, /^sensei\s*\(you\)/, /^boss/, /^kenji/, /^mika/, /^Result/]);
            await expect(mapOrder(table)).toHaveText(MAP_POOL_MAPS);
        }

        // (6 + 1 + 3.5) / 3, from the players who rated it.
        await expect(rowCells(sideTable(page, 'T'), 'Nuke')).toHaveText(['', '3.5', '6', '1', '3.50']);
        await expect(rowCells(sideTable(page, 'CT'), 'Nuke').last()).toHaveText('9.00');
        // A map nobody rated shows no result instead of an error.
        await expect(rowCells(sideTable(page, 'T'), 'Mirage').last()).toHaveText(/^–\s*Not rated$/);
    });

    test('only the signed-in player\'s own column can be edited', async ({ page, api }) => {
        withRatings(api, [rating('kenji', 'T', 'Mirage', 7)]);
        await open(page, api);

        const table = sideTable(page, 'T');
        await expect(table.getByRole('textbox')).toHaveCount(MAP_POOL_MAPS.length);
        await expect(table.getByRole('textbox', { name: 'Your T side rating for Mirage' })).toBeEditable();
        // Teammates' ratings are plain text.
        await expect(rowCells(table, 'Mirage').nth(2).getByRole('textbox')).toHaveCount(0);
        await expect(rowCells(table, 'Mirage').nth(2)).toHaveText('7');
    });

    test('typing a rating with one decimal saves it and moves down like a spreadsheet', async ({ page, api }) => {
        withRatings(api, [rating('kenji', 'T', 'Mirage', 7)]);
        await open(page, api);
        const table = sideTable(page, 'T');

        await table.getByRole('textbox', { name: 'Your T side rating for Mirage' }).fill('6.5');
        await page.keyboard.press('Enter');

        await expect(focused(page)).toHaveAccessibleName('Your T side rating for Nuke');
        await expect.poll(() => api.requestsTo('PUT', /map-pool/).map(r => [r.path, r.body])).toEqual([
            ['/teams/1/map-pool/T/Mirage', { rating: 6.5 }],
        ]);
        await expect(rowCells(table, 'Mirage').last()).toHaveText('6.75');

        // A comma works as the decimal point, and leaving the cell saves too.
        await focused(page).fill('7,8');
        await page.getByRole('heading', { name: 'Map Pool', level: 1 }).click();
        await expect.poll(() => api.requestsTo('PUT', /map-pool/).map(r => r.body)).toEqual([{ rating: 6.5 }, { rating: 7.8 }]);
        await expect(table.getByRole('textbox', { name: 'Your T side rating for Nuke' })).toHaveValue('7.8');

        // Both stick after a reload.
        await page.reload();
        await expect(sideTable(page, 'T').getByRole('textbox', { name: 'Your T side rating for Mirage' })).toHaveValue('6.5');
        expect(api.teams[0].mapRatings).toContainEqual(rating(ME.id, 'T', 'Nuke', 7.8));
    });

    test('ratings outside 1 to 10 or with two decimals are refused before sending', async ({ page, api }) => {
        withRatings(api);
        await open(page, api);
        const cell = sideTable(page, 'CT').getByRole('textbox', { name: 'Your CT side rating for Inferno' });

        for (const [typed, message] of [
            ['6.55', 'Use at most one decimal, like 6.5.'],
            ['11', 'Enter a number from 1 to 10.'],
            ['0.5', 'Enter a number from 1 to 10.'],
            ['abc', 'Enter a number from 1 to 10.'],
        ]) {
            await cell.fill(typed);
            await cell.press('Enter');
            await expect(cell).toHaveAttribute('aria-invalid', 'true');
            await expect(cell).toHaveAccessibleDescription(message);
        }

        // Escape gives up on the entry.
        await cell.press('Escape');
        await expect(cell).toHaveValue('');
        await expect(cell).not.toHaveAttribute('aria-invalid');
        expect(api.requestsTo('PUT', /map-pool/)).toHaveLength(0);
    });

    test('emptying a cell clears the rating', async ({ page, api }) => {
        withRatings(api, [rating(ME.id, 'CT', 'Ancient', 4)]);
        await open(page, api);
        const cell = sideTable(page, 'CT').getByRole('textbox', { name: 'Your CT side rating for Ancient' });
        await expect(cell).toHaveValue('4');

        await cell.fill('');
        await cell.press('Enter');

        await expect.poll(() => api.requestsTo('DELETE', /map-pool/).map(r => r.path)).toEqual(['/teams/1/map-pool/CT/Ancient']);
        await expect(rowCells(sideTable(page, 'CT'), 'Ancient').last()).toHaveText(/Not rated/);
        // Leaving a cell unchanged sends nothing.
        await cell.focus();
        await cell.press('Enter');
        expect(api.requestsTo('PUT', /map-pool/)).toHaveLength(0);
        expect(api.requestsTo('DELETE', /map-pool/)).toHaveLength(1);
    });

    test('a failed save puts the previous rating back and says why', async ({ page, api }) => {
        withRatings(api, [rating(ME.id, 'T', 'Dust2', 5)]);
        api.fail('PUT', /map-pool/, 500, 'The server is having a moment.');
        await open(page, api);
        const cell = sideTable(page, 'T').getByRole('textbox', { name: 'Your T side rating for Dust2' });

        await cell.fill('9');
        await cell.press('Enter');

        await expect(page.getByText('The server is having a moment.')).toBeVisible();
        await expect(cell).toHaveValue('5');
        await expect(rowCells(sideTable(page, 'T'), 'Dust2').last()).toHaveText('5.00');
    });

    test('quick changes to one cell reach the server in the order they were typed', async ({ page, api }) => {
        withRatings(api);
        const first = api.hold('PUT', /map-pool\/T\/Overpass$/);
        await open(page, api);
        const cell = sideTable(page, 'T').getByRole('textbox', { name: 'Your T side rating for Overpass' });

        await cell.fill('3');
        await cell.press('Enter');
        await first.requested;
        await cell.fill('8');
        await cell.press('Enter');

        // The second save waits for the first to finish.
        await page.waitForTimeout(200);
        expect(api.requestsTo('PUT', /Overpass/)).toHaveLength(1);
        first.release();

        await expect.poll(() => api.requestsTo('PUT', /Overpass/).map(r => r.body)).toEqual([{ rating: 3 }, { rating: 8 }]);
        await expect(cell).toHaveValue('8');
        expect(api.teams[0].mapRatings).toEqual([rating(ME.id, 'T', 'Overpass', 8)]);
    });

    test('anyone can sort, each side on its own, without telling the server', async ({ page, api }) => {
        withRatings(api, [
            rating('kenji', 'T', 'Nuke', 2), rating('kenji', 'T', 'Dust2', 9), rating('kenji', 'T', 'Mirage', 5),
            rating('mika', 'T', 'Nuke', 4), rating('mika', 'T', 'Mirage', 9),
            rating('kenji', 'CT', 'Train', 8),
        ]);
        await open(page, api);
        const t = sideTable(page, 'T');
        const requestsBefore = api.requests.length;

        // Results: Dust2 9, Mirage 7, Nuke 3; unrated maps keep their order at the end.
        await t.getByRole('button', { name: /^Result/ }).click();
        await expect(t.getByRole('columnheader', { name: /^Result/ })).toHaveAttribute('aria-sort', 'descending');
        await expect(mapOrder(t)).toHaveText(['Dust2', 'Mirage', 'Nuke', 'Ancient', 'Anubis', 'Inferno', 'Overpass', 'Train', 'Vertigo']);

        await t.getByRole('button', { name: /^Result/ }).click();
        await expect(t.getByRole('columnheader', { name: /^Result/ })).toHaveAttribute('aria-sort', 'ascending');
        await expect(mapOrder(t)).toHaveText(['Nuke', 'Mirage', 'Dust2', 'Ancient', 'Anubis', 'Inferno', 'Overpass', 'Train', 'Vertigo']);

        // By one player's column, best first.
        await t.getByRole('button', { name: /^mika/ }).click();
        await expect(mapOrder(t).first()).toHaveText('Mirage');

        // The CT table keeps its own order.
        await expect(mapOrder(sideTable(page, 'CT'))).toHaveText(MAP_POOL_MAPS);
        expect(api.requests.length).toBe(requestsBefore);

        // Remembered in this browser.
        await page.reload();
        await expect(sideTable(page, 'T').getByRole('columnheader', { name: /^mika/ })).toHaveAttribute('aria-sort', 'descending');
        await expect(mapOrder(sideTable(page, 'T')).first()).toHaveText('Mirage');

        // Map sorts A to Z again.
        await sideTable(page, 'T').getByRole('button', { name: /^Map/ }).click();
        await expect(mapOrder(sideTable(page, 'T'))).toHaveText(MAP_POOL_MAPS);
    });

    test('refresh brings in teammates\' new ratings', async ({ page, api }) => {
        const t = withRatings(api);
        await open(page, api);

        t.mapRatings.push(rating('kenji', 'CT', 'Vertigo', 6));
        await page.getByRole('button', { name: 'Refresh' }).click();

        await expect(rowCells(sideTable(page, 'CT'), 'Vertigo').last()).toHaveText('6.00');
    });

    test('is in the main navigation', async ({ page, api }) => {
        withRatings(api);
        await api.install(page);
        await page.goto('/dashboard');

        await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Map Pool' }).first().click();

        await expect(page).toHaveURL(/\/map-pool$/);
        await expect(page.getByRole('heading', { name: 'Map Pool', level: 1 })).toBeVisible();
    });
});
