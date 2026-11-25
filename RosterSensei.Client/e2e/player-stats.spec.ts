import { test, expect, chooseOption } from './support/fixtures';
import { ME, finishedMatch, member, team, type MockApi } from './support/mock-api';
import type { LineupEntry, Match } from '../src/types';

const daysAgo = (days: number) => new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString();

type Line = Partial<Pick<LineupEntry, 'rating' | 'kills' | 'deaths' | 'assists' | 'adr' | 'headshotPercentage' | 'role'>>;

const played = (userId: string, line: Line = {}, username = userId): LineupEntry => ({
    userId,
    username,
    role: null,
    played: true,
    rating: null,
    kills: null,
    deaths: null,
    assists: null,
    adr: null,
    headshotPercentage: null,
    ...line,
});

/**
 * A win and a loss:
 * kenji 25-15 then 10-20 (35/35, K/D 1.00), ADR 100.0 and 70.0, HS% 50 and 40, rated 1.40 and 0.80.
 * mika 18-12 in the win only, no rating. sensei played the loss without any stats.
 */
function withScoreboards(api: MockApi) {
    const win: Match = {
        ...finishedMatch(1, 'Kyoto Kitsune', 13, 9, daysAgo(5)),
        notes: 'Good T sides.',
        lineup: [
            played('kenji', { kills: 25, deaths: 15, assists: 4, adr: 100, headshotPercentage: 50, rating: 1.4, role: 'AWP' }),
            played('mika', { kills: 18, deaths: 12, assists: 7, adr: 81.5, headshotPercentage: 44.4 }),
            { ...played('ren'), played: false },
        ],
    };
    const loss: Match = {
        ...finishedMatch(2, 'Osaka Oni', 7, 13, daysAgo(10)),
        lineup: [
            played('kenji', { kills: 10, deaths: 20, assists: 2, adr: 70, headshotPercentage: 40, rating: 0.8 }),
            played(ME.id, {}, ME.username),
        ],
    };
    const t = team(1, 'Tokyo Ronin', 'Captain', true, { matches: [win, loss] });
    t.members.push(member('ren', 'Player'));
    api.teams = [t];
}

test.describe('Entering player stats', () => {
    test('the Lineup dialog sends kills, deaths, assists, ADR and HS% with the rating', async ({ page, api }) => {
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
        await dialog.getByLabel('Kills of kenji').fill('24');
        await dialog.getByLabel('Deaths of kenji').fill('17');
        await dialog.getByLabel('Assists of kenji').fill('5');
        // A comma works as the decimal point, and every stat is optional.
        await dialog.getByLabel('Average damage per round of kenji').fill('91,3');
        await dialog.getByLabel('Rating of kenji').fill('1.12');
        await dialog.getByRole('checkbox', { name: 'ren', exact: true }).check();
        await dialog.getByLabel('Kills of ren').fill('3');
        // A substitute who didn't play has no stats.
        await dialog.getByRole('checkbox', { name: 'ren played' }).uncheck();
        await expect(dialog.getByLabel('Kills of ren')).toHaveCount(0);

        await dialog.getByRole('button', { name: 'Save lineup' }).click();
        await expect(dialog).toBeHidden();

        expect(api.requestsTo('PUT', /^\/Matches\/1\/7\/lineup$/).map(r => r.body)).toEqual([{
            players: [
                { userId: 'kenji', played: true, rating: 1.12, kills: 24, deaths: 17, assists: 5, adr: 91.3 },
                { userId: 'ren', played: false },
            ],
        }]);
    });

    test('the Lineup dialog explains a stat out of range instead of saving', async ({ page, api }) => {
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, { matches: [finishedMatch(7, 'Kyoto Kitsune', 13, 9, '2026-10-20T18:00:00Z')] })];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Set lineup for the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();
        await dialog.getByLabel('Headshot percentage of kenji').fill('120');
        await dialog.getByRole('button', { name: 'Save lineup' }).click();

        await expect(dialog.getByText('HS%: from 0 to 100.')).toBeVisible();
        await expect(dialog.getByLabel('Headshot percentage of kenji')).toHaveAttribute('aria-invalid', 'true');
        await dialog.getByLabel('Headshot percentage of kenji').fill('48.25');
        await expect(dialog.getByText('HS%: use at most one decimal.')).toBeVisible();
        await dialog.getByLabel('Headshot percentage of kenji').fill('');
        await dialog.getByLabel('Kills of kenji').fill('1.5');
        await expect(dialog.getByText('Kills: a whole number from 0 to 999.')).toBeVisible();
        expect(api.requestsTo('PUT', /lineup$/)).toHaveLength(0);
    });

    test('the Lineup table moves down a column with Enter, like a spreadsheet', async ({ page, api }) => {
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, { matches: [finishedMatch(7, 'Kyoto Kitsune', 13, 9, '2026-10-20T18:00:00Z')] })];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Set lineup for the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        for (const name of ['sensei', 'kenji', 'mika']) await dialog.getByRole('checkbox', { name, exact: true }).check();

        // Kills straight down the column, Tab across to deaths.
        await dialog.getByLabel('Kills of sensei').fill('20');
        await page.keyboard.press('Enter');
        await expect(dialog.getByLabel('Kills of kenji')).toBeFocused();
        await page.keyboard.type('15');
        await page.keyboard.press('Enter');
        await page.keyboard.type('9');
        await page.keyboard.press('ArrowUp');
        await expect(dialog.getByLabel('Kills of kenji')).toBeFocused();
        await page.keyboard.press('Tab');
        await expect(dialog.getByLabel('Deaths of kenji')).toBeFocused();

        // Enter on the last row saves.
        await dialog.getByLabel('Kills of mika').press('Enter');
        await expect(dialog).toBeHidden();
        const [request] = api.requestsTo('PUT', /lineup$/);
        expect((request.body as { players: { kills?: number }[] }).players.map(p => p.kills)).toEqual([20, 15, 9]);
    });

    test('the Lineup dialog has no stats for a match without a result', async ({ page, api }) => {
        const t = team(1, 'Tokyo Ronin', 'Captain', true);
        t.matches = [{ ...finishedMatch(8, 'Osaka Oni', 0, 0, daysAgo(-3)), status: 'Scheduled', finalScore: null, matchOutcome: null, maps: [] }];
        api.teams = [t];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Set lineup for the match vs Osaka Oni' }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Osaka Oni' });
        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();
        await expect(dialog.getByLabel('Kills of kenji')).toHaveCount(0);
    });

    test('editing a finished match keeps the stats from the lineup', async ({ page, api }) => {
        withScoreboards(api);
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Edit the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: /Edit match/ });
        await dialog.getByLabel('Rating of kenji').fill('1.45');
        await dialog.getByRole('button', { name: /Save/ }).click();
        await expect(dialog).toBeHidden();

        const [request] = api.requestsTo('PUT', /^\/Matches\/1\/1$/);
        expect((request.body as { lineup: unknown }).lineup).toEqual([
            { userId: 'kenji', role: 'AWP', played: true, rating: 1.45, kills: 25, deaths: 15, assists: 4, adr: 100, headshotPercentage: 50 },
            { userId: 'mika', played: true, kills: 18, deaths: 12, assists: 7, adr: 81.5, headshotPercentage: 44.4 },
            { userId: 'ren', played: false },
        ]);
    });
});

test.describe('Match details', () => {
    test('shows the scoreboard with the team totals to every member', async ({ page, api }) => {
        withScoreboards(api);
        api.teams[0].role = 'Player';
        api.teams[0].isOwner = false;
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Details of the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'vs Kyoto Kitsune' });
        await expect(dialog.getByText('13 - 9')).toBeVisible();

        const rows = dialog.getByRole('table').getByRole('row');
        // K, D, A, +/-, K/D, ADR, HS%, rating.
        await expect(rows.filter({ hasText: 'kenji' }).getByRole('cell')).toHaveText(['25', '15', '4', '+10', '1.67', '100.0', '50.0%', '1.40']);
        await expect(rows.filter({ hasText: 'mika' }).getByRole('cell')).toHaveText(['18', '12', '7', '+6', '1.50', '81.5', '44.4%', '–']);
        // Added up, and the average of the players for ADR, HS% and rating.
        await expect(rows.filter({ hasText: 'Team' }).getByRole('cell')).toHaveText(['43', '27', '11', '+16', '1.59', '90.8', '47.2%', '1.40']);
        await expect(dialog.getByText('Substitutes: ren')).toBeVisible();
        await expect(dialog.getByText('Good T sides.')).toBeVisible();
        // A Player can look, but the lineup is changed by the Owner, Captain or Officers.
        await expect(dialog.getByRole('button', { name: /lineup/ })).toHaveCount(0);
    });

    test('leads a manager on to the lineup when nothing is recorded yet', async ({ page, api }) => {
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, { matches: [{ ...finishedMatch(3, 'Nara Ninja', 13, 4, daysAgo(2)), lineup: [played('kenji')] }] })];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Details of the match vs Nara Ninja' }).click();
        const dialog = page.getByRole('dialog', { name: 'vs Nara Ninja' });
        await expect(dialog.getByText('No stats recorded yet. They can be added in the lineup.')).toBeVisible();
        await dialog.getByRole('button', { name: 'Edit lineup' }).click();

        await expect(page.getByRole('dialog', { name: 'Lineup vs Nara Ninja' })).toBeVisible();
    });
});

test.describe('Player statistics with stats', () => {
    const section = (page: import('@playwright/test').Page) => page.getByRole('region', { name: 'Player statistics', exact: true });
    const players = (page: import('@playwright/test').Page) => section(page).getByRole('rowheader');

    test('shows win rate, K/D, ADR and HS% per player', async ({ page, api }) => {
        withScoreboards(api);
        await api.install(page);
        await page.goto('/statistics');

        // Best average rating first; players without a rating go last.
        await expect(players(page)).toHaveText(['kenji', 'mika', 'sensei']);
        // Matches, win rate, K/D, ADR, HS%, rating, form, best, worst.
        await expect(section(page).getByRole('row', { name: /^kenji/ }).getByRole('cell')).toHaveText(['2', '50%', '1.00', '85.0', '45.0%', '1.10', '1.10', '1.40', '0.80']);
        await expect(section(page).getByRole('row', { name: /^mika/ }).getByRole('cell')).toHaveText(['1', '100%', '1.50', '81.5', '44.4%', '–', '–', '–', '–']);
        await expect(section(page).getByRole('row', { name: /^sensei/ }).getByRole('cell')).toHaveText(['1', '0%', '–', '–', '–', '–', '–', '–', '–']);

        await section(page).getByRole('button', { name: 'K/D' }).click();
        await expect(players(page)).toHaveText(['mika', 'kenji', 'sensei']);
        // Lowest first, and still the player without a K/D last.
        await section(page).getByRole('button', { name: 'K/D' }).click();
        await expect(players(page)).toHaveText(['kenji', 'mika', 'sensei']);

        await section(page).getByRole('button', { name: 'Win rate' }).click();
        await expect(players(page)).toHaveText(['mika', 'kenji', 'sensei']);

        await chooseOption(page, section(page).getByRole('combobox', { name: 'Matches played' }), 'At least 3');
        await expect(section(page).getByText('No players match these filters')).toBeVisible();
    });
});
