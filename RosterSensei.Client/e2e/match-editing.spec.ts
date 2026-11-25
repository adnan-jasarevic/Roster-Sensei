import { test, expect, chooseOption } from './support/fixtures';
import { finishedMatch, member, team, type MockApi } from './support/mock-api';
import type { Match } from '../src/types';

const daysAgo = (days: number) => new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString();

/** A Bo3 scheduled two days ago with its expected lineup: shown as "Needs result". */
function overdueMatch(): Match {
    return {
        ...finishedMatch(5, 'Kyoto Kitsune', 0, 0, daysAgo(2)),
        status: 'Scheduled',
        playedDate: undefined,
        finalScore: null,
        matchOutcome: null,
        bestOf: 3,
        maps: [{ mapName: 'Mirage' }, { mapName: 'Nuke' }, { mapName: 'Inferno' }],
        lineup: [
            { userId: 'kenji', username: 'kenji', role: 'AWP', played: true, rating: null },
            { userId: 'mika', username: 'mika', role: 'IGL', played: true, rating: null },
            { userId: 'ren', username: 'ren', role: null, played: false, rating: null },
        ],
        version: 3,
    };
}

function withMatches(api: MockApi, role: 'Captain' | 'Player' = 'Captain') {
    const done = { ...finishedMatch(6, 'Osaka Oni', 13, 9, daysAgo(10)), tournament: 'Local LAN Cup', version: 1 };
    const t = team(1, 'Tokyo Ronin', role, role === 'Captain', { matches: [overdueMatch(), done] });
    t.members.push(member('ren', 'Player'));
    api.teams = [t];
}

test.describe('Editing matches', () => {
    test('a match that needs a result gets it from "Add result"', async ({ page, api }) => {
        withMatches(api);
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Add result for the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'Add result vs Kyoto Kitsune' });

        // Starts from the match as it is, with the status already on Finished.
        await expect(dialog.getByRole('textbox', { name: /^Opponent/ })).toHaveValue('Kyoto Kitsune');
        await expect(dialog.getByRole('combobox', { name: 'Status' })).toHaveText('Finished');
        await expect(dialog.getByRole('checkbox', { name: 'kenji played' })).toBeChecked();
        await expect(dialog.getByRole('checkbox', { name: 'ren played' })).not.toBeChecked();

        await dialog.getByLabel('Mirage our final score').fill('13');
        await dialog.getByLabel('Mirage opponent final score').fill('8');
        await dialog.getByLabel('Nuke our final score').fill('13');
        await dialog.getByLabel('Nuke opponent final score').fill('11');
        await expect(dialog.getByText('2 - 0', { exact: true })).toBeVisible();
        await dialog.getByLabel('Rating of kenji').fill('1,31');
        await dialog.getByRole('button', { name: 'Save result' }).click();
        await expect(dialog).toBeHidden();
        await expect(page.getByText('Result saved for the match vs Kyoto Kitsune.')).toBeVisible();

        const [request] = api.requestsTo('PUT', /^\/Matches\/1\/5$/);
        expect(request.body).toMatchObject({
            version: 3,
            status: 2,
            bestOf: 3,
            maps: [
                { mapName: 'Mirage', ourScore: 13, opponentScore: 8 },
                { mapName: 'Nuke', ourScore: 13, opponentScore: 11 },
                { mapName: 'Inferno' },
            ],
            // Roles and the substitute are kept, even though the dialog only shows who played.
            lineup: [
                { userId: 'kenji', role: 'AWP', played: true, rating: 1.31 },
                { userId: 'mika', role: 'IGL', played: true },
                { userId: 'ren', played: false },
            ],
        });

        // The row is now a result, and no longer offers "Add result".
        await expect(page.getByRole('button', { name: 'Add result for the match vs Kyoto Kitsune' })).toHaveCount(0);
        await page.getByRole('radio', { name: 'Needs result' }).check({ force: true });
        await expect(page.getByText('Nothing is waiting for a result')).toBeVisible();
    });

    test('a finished match opens filled in and saves corrections', async ({ page, api }) => {
        withMatches(api);
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Edit the match vs Osaka Oni' }).click();
        const dialog = page.getByRole('dialog', { name: 'Edit match vs Osaka Oni' });
        await expect(dialog.getByLabel('Tournament')).toHaveValue('Local LAN Cup');
        await expect(dialog.getByLabel('Mirage our final score')).toHaveValue('13');

        await dialog.getByLabel('Mirage opponent final score').fill('11');
        await dialog.getByRole('button', { name: 'Save changes' }).click();
        await expect(dialog).toBeHidden();

        const [request] = api.requestsTo('PUT', /^\/Matches\/1\/6$/);
        expect(request.body).toMatchObject({ version: 1, status: 2, tournament: 'Local LAN Cup', maps: [{ mapName: 'Mirage', ourScore: 13, opponentScore: 11 }] });
        await expect(page.getByText('13-11')).toBeVisible();
    });

    test('a match can be cancelled, which sends no lineup and no scores', async ({ page, api }) => {
        withMatches(api);
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Edit the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'Edit match vs Kyoto Kitsune' });
        await chooseOption(page, dialog.getByRole('combobox', { name: 'Status' }), 'Cancelled');
        // Only a finished match asks who played.
        await expect(dialog.getByRole('checkbox', { name: 'kenji played' })).toHaveCount(0);
        await dialog.getByRole('button', { name: 'Save changes' }).click();
        await expect(dialog).toBeHidden();

        const [request] = api.requestsTo('PUT', /^\/Matches\/1\/5$/);
        expect(request.body).toMatchObject({ status: 3, ourScore: 0, opponentScore: 0 });
        expect((request.body as { lineup?: unknown }).lineup).toBeUndefined();
    });

    test('a save over someone else\'s newer edit is refused and the list reloads', async ({ page, api }) => {
        withMatches(api);
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Edit the match vs Osaka Oni' }).click();
        const dialog = page.getByRole('dialog', { name: 'Edit match vs Osaka Oni' });
        // Someone else saves the match while the form is open.
        api.teams[0].matches.find(m => m.id === 6)!.version = 2;
        const loadsBefore = api.requestsTo('GET', /^\/Matches\/1$/).length;

        await dialog.getByRole('button', { name: 'Save changes' }).click();

        await expect(dialog.getByText('Someone else changed this match while you were editing it. Open it again to see their changes.')).toBeVisible();
        await expect.poll(() => api.requestsTo('GET', /^\/Matches\/1$/).length).toBeGreaterThan(loadsBefore);
    });

    test('players see the matches but cannot edit them', async ({ page, api }) => {
        withMatches(api, 'Player');
        await api.install(page);
        await page.goto('/matches');

        await expect(page.getByText('vs Kyoto Kitsune')).toBeVisible();
        await expect(page.getByRole('button', { name: /^Edit the match/ })).toHaveCount(0);
        await expect(page.getByRole('button', { name: /^Add result/ })).toHaveCount(0);
    });
});
