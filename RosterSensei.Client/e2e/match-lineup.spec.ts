import { test, expect } from './support/fixtures';
import { finishedMatch, member, team, type MockApi } from './support/mock-api';

function withMatch(api: MockApi, role: 'Captain' | 'Player' = 'Captain') {
    const t = team(1, 'Tokyo Ronin', role, role === 'Captain', {
        matches: [finishedMatch(7, 'Kyoto Kitsune', 13, 9, '2026-10-20T18:00:00Z')],
    });
    t.members.push(member('ren', 'Player'), member('yuki', 'Player'));
    api.teams = [t];
    return t;
}

test.describe('Match lineup', () => {
    test('sets a lineup with roles and a substitute', async ({ page, api }) => {
        withMatch(api);
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Set lineup for the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });

        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();
        await dialog.getByRole('checkbox', { name: 'mika', exact: true }).check();
        await dialog.getByRole('checkbox', { name: 'ren', exact: true }).check();
        await dialog.getByLabel('Role of kenji').fill('  AWP ');
        await dialog.getByLabel('Role of mika').fill('IGL');
        await dialog.getByRole('checkbox', { name: 'ren played' }).uncheck();
        await expect(dialog.getByText('3 of 10 selected · 2 played')).toBeVisible();

        await dialog.getByRole('button', { name: 'Save lineup' }).click();
        await expect(dialog).toBeHidden();

        expect(api.requestsTo('PUT', /^\/Matches\/1\/7\/lineup$/).map(r => r.body)).toEqual([{
            players: [
                { userId: 'kenji', role: 'AWP', played: true },
                { userId: 'mika', role: 'IGL', played: true },
                { userId: 'ren', played: false },
            ],
        }]);

        const lineup = page.getByRole('list', { name: 'Lineup vs Kyoto Kitsune' });
        await expect(lineup.getByRole('listitem')).toHaveText(['kenjiAWP', 'mikaIGL', "ren(sub, didn't play)"]);
        await expect(page.getByRole('button', { name: 'Edit lineup for the match vs Kyoto Kitsune' })).toBeVisible();
    });

    test('editing starts from the saved lineup, and unticking everyone clears it', async ({ page, api }) => {
        const t = withMatch(api);
        t.matches[0].lineup = [{ userId: 'kenji', username: 'kenji', role: 'AWP', played: true }];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Edit lineup for the match vs Kyoto Kitsune' }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        await expect(dialog.getByLabel('Role of kenji')).toHaveValue('AWP');

        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).uncheck();
        await dialog.getByRole('button', { name: 'Save lineup' }).click();
        await expect(dialog).toBeHidden();

        expect(api.requestsTo('PUT', /lineup$/).at(-1)?.body).toEqual({ players: [] });
        await expect(page.getByText('No lineup recorded.')).toBeVisible();
    });

    test('caps the lineup at ten players', async ({ page, api }) => {
        const t = withMatch(api);
        for (let i = 1; i <= 8; i++) t.members.push(member(`p${i}`, 'Player'));
        await api.install(page);
        await page.goto('/matches');
        await page.getByRole('button', { name: /Set lineup/ }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });

        const boxes = dialog.getByRole('checkbox', { name: /^(sensei|kenji|mika|ren|yuki|p\d)$/ });
        await expect(boxes).toHaveCount(13);
        for (let i = 0; i < 10; i++) await boxes.nth(i).check();

        await expect(dialog.getByText('10 of 10 selected')).toBeVisible();
        await expect(boxes.nth(10)).toBeDisabled();
        await boxes.nth(0).uncheck();
        await expect(boxes.nth(10)).toBeEnabled();
    });

    test('role input stops at 30 characters', async ({ page, api }) => {
        withMatch(api);
        await api.install(page);
        await page.goto('/matches');
        await page.getByRole('button', { name: /Set lineup/ }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();
        const role = dialog.getByLabel('Role of kenji');
        await role.fill('x'.repeat(40));
        await expect(role).toHaveValue('x'.repeat(30));
    });

    test('a player who left the team is flagged and dropped on save', async ({ page, api }) => {
        const t = withMatch(api);
        t.matches[0].lineup = [
            { userId: 'kenji', username: 'kenji', role: null, played: true },
            { userId: 'gone', username: 'ghost', role: 'Entry', played: true },
        ];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: /Edit lineup/ }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        await expect(dialog.getByText('ghost is no longer on the team')).toBeVisible();
        await expect(dialog.getByText('1 of 10 selected')).toBeVisible();

        await dialog.getByRole('button', { name: 'Save lineup' }).click();
        await expect(dialog).toBeHidden();
        expect(api.requestsTo('PUT', /lineup$/)[0].body).toEqual({ players: [{ userId: 'kenji', played: true }] });
    });

    test('a failed save keeps the dialog open with the error', async ({ page, api }) => {
        withMatch(api);
        api.fail('PUT', /lineup$/, 409, 'Someone else changed this lineup at the same time. Refresh and try again.');
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: /Set lineup/ }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();
        await dialog.getByRole('button', { name: 'Save lineup' }).click();

        await expect(dialog.getByRole('alert')).toHaveText(/Someone else changed this lineup/);
        await expect(dialog.getByRole('checkbox', { name: 'kenji', exact: true })).toBeChecked();
    });

    test('the dialog cannot be closed while saving', async ({ page, api }) => {
        withMatch(api);
        await api.install(page);
        await page.goto('/matches');
        await page.getByRole('button', { name: /Set lineup/ }).click();
        const dialog = page.getByRole('dialog', { name: 'Lineup vs Kyoto Kitsune' });
        await dialog.getByRole('checkbox', { name: 'kenji', exact: true }).check();

        const held = api.hold('PUT', /lineup$/);
        await dialog.getByRole('button', { name: 'Save lineup' }).click();
        await held.requested;
        await page.keyboard.press('Escape');
        await expect(dialog).toBeVisible();
        await expect(dialog.getByRole('button', { name: 'Close' })).toBeDisabled();

        held.release();
        await expect(dialog).toBeHidden();
        await expect.poll(() => api.requestsTo('PUT', /lineup$/).length).toBe(1);
    });

    test('players see the lineup but cannot edit it', async ({ page, api }) => {
        const t = withMatch(api, 'Player');
        t.matches[0].lineup = [{ userId: 'kenji', username: 'kenji', role: 'AWP', played: true }];
        await api.install(page);
        await page.goto('/matches');

        await expect(page.getByRole('list', { name: 'Lineup vs Kyoto Kitsune' })).toContainText('kenji');
        await expect(page.getByRole('button', { name: /lineup/i })).toHaveCount(0);
    });
});
