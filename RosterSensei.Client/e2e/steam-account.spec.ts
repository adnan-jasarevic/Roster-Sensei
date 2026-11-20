import { test, expect } from './support/fixtures';
import { member, team } from './support/mock-api';

const STEAM_ID = '76561198012345678';
const PROFILE_URL = `https://steamcommunity.com/profiles/${STEAM_ID}`;
const AVATAR = 'https://avatars.steamstatic.com/abc_medium.jpg';

test.describe('Steam account', () => {
    test('links an account by signing in through Steam', async ({ page, api }) => {
        api.steamProfile = { personaName: 'kenji-awp', avatarUrl: null };
        await api.install(page);
        const steamOpened = await api.fakeSteam(page);
        await page.goto('/account');

        await page.getByRole('button', { name: 'Sign in through Steam' }).click();

        await expect(page.getByText('kenji-awp')).toBeVisible();
        await expect(page.getByText('Verified', { exact: true })).toBeVisible();
        await expect(page.getByText(`SteamID64 ${STEAM_ID}`)).toBeVisible();
        await expect(page.getByRole('link', { name: /Steam profile/ })).toHaveAttribute('href', PROFILE_URL);

        // Steam was asked to come back to the account page, and the reply was cleared from the address bar.
        expect(steamOpened).toHaveLength(1);
        expect(api.requestsTo('POST', /^\/Account\/steam\/sign-in$/).map(r => r.body))
            .toEqual([{ returnUrl: new URL('/account', page.url()).toString() }]);
        await expect(page).toHaveURL(/\/account$/);
        // Sent once, even under StrictMode.
        const completes = api.requestsTo('POST', /^\/Account\/steam\/sign-in\/complete$/);
        expect(completes).toHaveLength(1);
        expect((completes[0].body as { parameters: Record<string, string> }).parameters['openid.mode']).toBe('id_res');
    });

    test('cancelling at Steam says so and links nothing', async ({ page, api }) => {
        await api.install(page);
        await api.fakeSteam(page, { cancel: true });
        await page.goto('/account');

        await page.getByRole('button', { name: 'Sign in through Steam' }).click();

        await expect(page.getByRole('alert')).toHaveText('Steam sign-in was cancelled.');
        await expect(page.getByRole('button', { name: 'Sign in through Steam' })).toBeVisible();
        expect(api.steamId).toBeNull();
    });

    test('a failed start shows the error and stays on the page', async ({ page, api }) => {
        api.fail('POST', /^\/Account\/steam\/sign-in$/, 400, "Steam sign-in can't return to this site.");
        await api.install(page);
        await page.goto('/account');

        await page.getByRole('button', { name: 'Sign in through Steam' }).click();

        await expect(page.getByRole('alert')).toHaveText("Steam sign-in can't return to this site.");
        await expect(page).toHaveURL(/\/account$/);
    });

    test('a pasted link shows as unverified and can be verified', async ({ page, api }) => {
        api.steamId = '76561198000000099';
        api.steamVerified = false;
        await api.install(page);
        await api.fakeSteam(page);
        await page.goto('/account');

        await expect(page.getByText('Unverified', { exact: true })).toBeVisible();
        await expect(page.getByText(/isn't proven to be yours/)).toBeVisible();
        await expect(page.getByLabel(/Steam profile or ID/)).toHaveCount(0);

        await page.getByRole('button', { name: 'Verify with Steam' }).click();
        await expect(page.getByText('Verified', { exact: true })).toBeVisible();
        await expect(page.getByText(`SteamID64 ${STEAM_ID}`)).toBeVisible();
    });

    test('unlinks a linked account', async ({ page, api }) => {
        api.steamId = STEAM_ID;
        api.steamProfile = { personaName: 'kenji-awp', avatarUrl: AVATAR };
        await page.route(AVATAR, route => route.fulfill({ status: 404 }));
        await api.install(page);
        await page.goto('/account');
        await expect(page.getByText('kenji-awp')).toBeVisible();

        await page.getByRole('button', { name: 'Unlink' }).click();
        const dialog = page.getByRole('dialog', { name: 'Unlink Steam account' });
        await dialog.getByRole('button', { name: 'Unlink' }).click();
        await expect(dialog).toBeHidden();

        await expect(page.getByRole('button', { name: 'Sign in through Steam' })).toBeVisible();
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
        await expect(page.getByRole('button', { name: 'Sign in through Steam' })).toBeVisible();
    });

    test('the roster shows Steam names, avatars and unverified links', async ({ page, api }) => {
        await page.route(AVATAR, route => route.fulfill({ status: 404 }));
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, {
            members: [
                member('me', 'Captain', { username: 'sensei', isOwner: true }),
                member('kenji', 'Player', {
                    steamId: STEAM_ID, steamProfileUrl: PROFILE_URL, steamVerified: true,
                    steamPersonaName: 'kenji-awp', steamAvatarUrl: AVATAR,
                }),
                member('yuki', 'Player', { steamId: '76561198000000099', steamProfileUrl: 'https://steamcommunity.com/profiles/76561198000000099', steamVerified: false }),
                member('mika', 'Officer'),
            ],
        })];
        await api.install(page);
        await page.goto('/team');

        const link = page.getByRole('link', { name: /profile of kenji/ });
        await expect(link).toHaveAttribute('href', PROFILE_URL);
        await expect(link).toHaveAttribute('target', '_blank');
        await expect(link).toContainText('kenji-awp');
        await expect(page.getByRole('link', { name: /profile of yuki, unverified/ })).toContainText('Steam');
        await expect(page.getByRole('link', { name: /profile of mika/ })).toHaveCount(0);
    });

    test('the account page is reachable from the navigation', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/dashboard');
        await page.getByRole('link', { name: 'Account' }).first().click();
        await expect(page.getByRole('heading', { name: 'Account', level: 1 })).toBeVisible();
    });
});
