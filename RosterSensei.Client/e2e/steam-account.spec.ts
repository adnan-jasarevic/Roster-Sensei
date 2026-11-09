import { test, expect } from './support/fixtures';
import { member, team } from './support/mock-api';

const STEAM_ID = '76561198012345678';
const PROFILE_URL = `https://steamcommunity.com/profiles/${STEAM_ID}`;

test.describe('Steam account', () => {
    test('links an account from a profile URL and shows it', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/account');

        await page.getByLabel('Steam profile or ID').fill(`  ${PROFILE_URL}/  `);
        await page.getByRole('button', { name: 'Link Steam account' }).click();

        await expect(page.getByText(`SteamID64 ${STEAM_ID}`)).toBeVisible();
        await expect(page.getByRole('link', { name: /Steam profile/ })).toHaveAttribute('href', PROFILE_URL);
        await expect(page.getByLabel('Steam profile or ID')).toBeHidden();
        expect(api.requestsTo('PUT', /^\/Account\/steam$/).map(r => r.body)).toEqual([{ steam: `${PROFILE_URL}/` }]);
    });

    test('shows a validation error on the field', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/account');

        const field = page.getByLabel('Steam profile or ID');
        await field.fill('https://steamcommunity.com/id/someone');
        await page.getByRole('button', { name: 'Link Steam account' }).click();

        await expect(field).toHaveAttribute('aria-invalid', 'true');
        await expect(field).toHaveAccessibleDescription(/Enter your SteamID64/);
        // Typing again clears the error.
        await field.fill('7');
        await expect(field).not.toHaveAttribute('aria-invalid', 'true');
    });

    test('a Steam account linked to someone else shows the conflict', async ({ page, api }) => {
        api.fail('PUT', /^\/Account\/steam$/, 409, 'This Steam account is already linked to another user.');
        await api.install(page);
        await page.goto('/account');

        await page.getByLabel('Steam profile or ID').fill(STEAM_ID);
        await page.getByRole('button', { name: 'Link Steam account' }).click();
        await expect(page.getByRole('alert')).toHaveText('This Steam account is already linked to another user.');
    });

    test('changes and unlinks a linked account', async ({ page, api }) => {
        api.steamId = STEAM_ID;
        await api.install(page);
        await page.goto('/account');
        await expect(page.getByText(`SteamID64 ${STEAM_ID}`)).toBeVisible();

        await page.getByRole('button', { name: 'Change' }).click();
        await page.getByLabel('New Steam profile or ID').fill('76561198000000099');
        await page.getByRole('button', { name: 'Save Steam account' }).click();
        await expect(page.getByText('SteamID64 76561198000000099')).toBeVisible();

        await page.getByRole('button', { name: 'Unlink' }).click();
        const dialog = page.getByRole('dialog', { name: 'Unlink Steam account' });
        await dialog.getByRole('button', { name: 'Unlink' }).click();
        await expect(dialog).toBeHidden();

        await expect(page.getByLabel('Steam profile or ID')).toBeVisible();
        await expect(page.getByText(/SteamID64 \d/)).toBeHidden();
        expect(api.steamId).toBeNull();
    });

    test('a failed load can be retried', async ({ page, api }) => {
        const restore = api.fail('GET', /^\/Account\/steam$/);
        await api.install(page);
        await page.goto('/account');

        await expect(page.getByText("Couldn't load your Steam account")).toBeVisible();
        restore();
        await page.getByRole('button', { name: 'Try again' }).click();
        await expect(page.getByLabel('Steam profile or ID')).toBeVisible();
    });

    test('the roster links to members’ Steam profiles', async ({ page, api }) => {
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, {
            members: [
                member('me', 'Captain', { username: 'sensei', isOwner: true }),
                member('kenji', 'Player', { steamId: STEAM_ID, steamProfileUrl: PROFILE_URL }),
                member('mika', 'Officer'),
            ],
        })];
        await api.install(page);
        await page.goto('/team');

        const link = page.getByRole('link', { name: /profile of kenji/ });
        await expect(link).toHaveAttribute('href', PROFILE_URL);
        await expect(link).toHaveAttribute('target', '_blank');
        await expect(page.getByRole('link', { name: /profile of mika/ })).toHaveCount(0);
    });

    test('the account page is reachable from the navigation', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/dashboard');
        await page.getByRole('link', { name: 'Account' }).first().click();
        await expect(page.getByRole('heading', { name: 'Account', level: 1 })).toBeVisible();
    });
});
