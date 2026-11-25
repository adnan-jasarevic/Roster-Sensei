import { test, expect, chooseOption } from './support/fixtures';
import { ME, finishedMatch, member, team, type MockApi } from './support/mock-api';
import type { LineupEntry, Match } from '../src/types';

const daysAgo = (days: number) => new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString();

const played = (userId: string, rating: number | null, username = userId): LineupEntry => ({ userId, username, role: null, played: true, rating });

function match(id: number, days: number, map: string, type: Match['type'], lineup: LineupEntry[]): Match {
    const m = finishedMatch(id, `Opponent ${id}`, 13, 9, daysAgo(days));
    return { ...m, type, maps: [{ mapName: map, ourScore: 13, opponentScore: 9 }], lineup };
}

/**
 * kenji: 1.30, 1.10, 0.70, 2.10 (all time 1.30) · mika: 0.90, 1.20, 1.00 (1.03) · sensei: 1.10.
 * The match 400 days ago is outside "this year" and the shorter periods.
 */
function withRatedMatches(api: MockApi) {
    const t = team(1, 'Tokyo Ronin', 'Captain', true, {
        matches: [
            match(1, 5, 'Mirage', 'Official', [played('kenji', 1.3), played('mika', 0.9), played(ME.id, 1.1, ME.username)]),
            match(2, 20, 'Nuke', 'Scrim', [played('kenji', 1.1), played('mika', 1.2)]),
            match(3, 60, 'Mirage', 'Official', [played('kenji', 0.7), played('mika', 1.0), played(ME.id, null, ME.username), { userId: 'ren', username: 'ren', role: null, played: false, rating: null }]),
            match(4, 400, 'Inferno', 'Official', [played('kenji', 2.1)]),
        ],
    });
    api.teams = [t];
}

test.describe('Entering player ratings', () => {
    test('Add Match sends who played with their ratings, a comma works as the decimal point', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Add match' }).click();
        const dialog = page.getByRole('dialog', { name: 'Add match' });
        await dialog.getByLabel('Opponent').fill('Kyoto Kitsune');
        await dialog.getByLabel('Date and time').fill('2026-11-10T19:00');

        // Only a finished match asks who played.
        await expect(dialog.getByRole('group', { name: /Players/ })).toHaveCount(0);
        await chooseOption(page, dialog.getByRole('combobox', { name: 'Status' }), 'Finished');

        await dialog.getByRole('checkbox', { name: 'kenji played' }).check();
        await dialog.getByLabel('Rating of kenji').fill('1,25');
        await dialog.getByRole('checkbox', { name: 'mika played' }).check();
        await dialog.getByRole('button', { name: 'Add match' }).click();
        await expect(dialog).toBeHidden();

        const [request] = api.requestsTo('POST', /^\/Matches\/1$/);
        expect((request.body as { lineup: unknown }).lineup).toEqual([
            { userId: 'kenji', played: true, rating: 1.25 },
            { userId: 'mika', played: true },
        ]);
    });

    test('Add Match explains a rating out of range instead of saving', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Add match' }).click();
        const dialog = page.getByRole('dialog', { name: 'Add match' });
        await dialog.getByLabel('Opponent').fill('Kyoto Kitsune');
        await dialog.getByLabel('Date and time').fill('2026-11-10T19:00');
        await chooseOption(page, dialog.getByRole('combobox', { name: 'Status' }), 'Finished');
        await dialog.getByRole('checkbox', { name: 'kenji played' }).check();
        await dialog.getByLabel('Rating of kenji').fill('100');

        await dialog.getByRole('button', { name: 'Add match' }).click();

        await expect(dialog.getByText('Enter a rating from 0.01 to 99.99.')).toBeVisible();
        expect(api.requestsTo('POST', /^\/Matches\/1$/)).toHaveLength(0);

        await dialog.getByLabel('Rating of kenji').fill('1.234');
        await expect(dialog.getByText('Use at most two decimals, like 1.15.')).toBeVisible();
    });

    test('the Lineup dialog rates players who played in a finished match', async ({ page, api }) => {
        const t = team(1, 'Tokyo Ronin', 'Captain', true, {
            matches: [finishedMatch(7, 'Kyoto Kitsune', 13, 9, '2026-10-20T18:00:00Z')],
        });
        t.members.push(member('ren', 'Player'));
        api.teams = [t];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Set lineup for the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();
        await dialog.getByLabel('Rating of kenji').fill('0.95');
        await dialog.getByRole('checkbox', { name: 'ren', exact: true }).check();
        await dialog.getByLabel('Rating of ren').fill('1.5');
        // A substitute who didn't play has no rating.
        await dialog.getByRole('checkbox', { name: 'ren played' }).uncheck();
        await expect(dialog.getByLabel('Rating of ren')).toHaveCount(0);

        await dialog.getByRole('button', { name: 'Save lineup' }).click();
        await expect(dialog).toBeHidden();

        expect(api.requestsTo('PUT', /^\/Matches\/1\/7\/lineup$/).map(r => r.body)).toEqual([{
            players: [
                { userId: 'kenji', played: true, rating: 0.95 },
                { userId: 'ren', played: false },
            ],
        }]);
        await expect(page.getByRole('list', { name: 'Lineup vs Kyoto Kitsune' }).getByRole('listitem').first()).toHaveText('kenjirating 0.95');
    });

    test('the Lineup dialog has no ratings for a match without a result', async ({ page, api }) => {
        const scheduled: Match = { ...finishedMatch(8, 'Osaka Oni', 0, 0, '2026-12-20T18:00:00Z'), status: 'Scheduled', finalScore: null, matchOutcome: null, maps: [] };
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, { matches: [scheduled] })];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Set lineup for the match vs Osaka Oni' }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Osaka Oni' });
        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();

        await expect(dialog.getByLabel('Rating of kenji')).toHaveCount(0);
    });
});

test.describe('Player statistics', () => {
    const section = (page: import('@playwright/test').Page) => page.getByRole('region', { name: 'Player statistics', exact: true });
    const players = (page: import('@playwright/test').Page) => section(page).getByRole('rowheader');

    test('lists every player who played, best average rating first, and the headers sort', async ({ page, api }) => {
        withRatedMatches(api);
        await api.install(page);
        await page.goto('/statistics');

        await expect(players(page)).toHaveText(['kenji', 'sensei', 'mika']);
        const kenji = section(page).getByRole('row', { name: /^kenji/ });
        // Matches, win rate, K/D, ADR, HS% (none recorded here), rating, form (last 5), best, worst.
        await expect(kenji.getByRole('cell')).toHaveText(['4', '100%', '–', '–', '–', '1.30', '1.30', '2.10', '0.70']);
        await expect(section(page).getByText('3 players · 4 matches')).toBeVisible();

        await section(page).getByRole('button', { name: 'Player' }).click();
        await expect(players(page)).toHaveText(['kenji', 'mika', 'sensei']);
        await section(page).getByRole('button', { name: 'Matches' }).click();
        await expect(players(page)).toHaveText(['kenji', 'mika', 'sensei']);
        await expect(section(page).getByRole('columnheader', { name: 'Matches' })).toHaveAttribute('aria-sort', 'descending');
    });

    test('filters by period, map and number of matches played', async ({ page, api }) => {
        withRatedMatches(api);
        await api.install(page);
        await page.goto('/statistics');
        await expect(players(page)).toHaveText(['kenji', 'sensei', 'mika']);

        await section(page).getByRole('radio', { name: '30 days' }).check({ force: true });
        await expect(players(page)).toHaveText(['kenji', 'sensei', 'mika']);
        await expect(section(page).getByRole('row', { name: /^kenji/ }).getByRole('cell').nth(5)).toHaveText('1.20');

        await section(page).getByRole('radio', { name: 'All time' }).check({ force: true });
        await chooseOption(page, section(page).getByRole('combobox', { name: 'Map' }), 'Mirage');
        await expect(players(page)).toHaveText(['sensei', 'kenji', 'mika']);

        await chooseOption(page, section(page).getByRole('combobox', { name: 'Map' }), 'All maps');
        // sensei played two matches (one unrated), so needs "any number".
        await chooseOption(page, section(page).getByRole('combobox', { name: 'Matches played' }), 'At least 3');
        await expect(players(page)).toHaveText(['kenji', 'mika']);

        await chooseOption(page, section(page).getByRole('combobox', { name: 'Matches played' }), 'At least 10');
        await expect(section(page).getByText('No players match these filters')).toBeVisible();
        await section(page).getByRole('button', { name: 'Clear filters' }).first().click();
        await expect(players(page)).toHaveText(['kenji', 'sensei', 'mika']);
    });

    test('follows the match type switch at the top of the tab', async ({ page, api }) => {
        withRatedMatches(api);
        await api.install(page);
        await page.goto('/statistics');

        await page.getByRole('radio', { name: 'Scrims' }).check({ force: true });

        await expect(players(page)).toHaveText(['mika', 'kenji']);
    });

    test('explains where player statistics come from when no lineup is recorded yet', async ({ page, api }) => {
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, { matches: [finishedMatch(1, 'Kyoto Kitsune', 13, 9, daysAgo(3))] })];
        await api.install(page);
        await page.goto('/statistics');

        await expect(section(page).getByText('No player statistics yet')).toBeVisible();
    });
});
