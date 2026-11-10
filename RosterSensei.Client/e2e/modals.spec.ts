import { test, expect } from './support/fixtures';

test.describe('modal focus', () => {
    test('the invite dialog starts on the email field, keeps focus inside and returns it to the opener', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/team');

        const opener = page.getByRole('button', { name: 'Invite player' });
        await opener.click();
        const dialog = page.getByRole('dialog', { name: 'Invite player' });
        await expect(dialog).toBeVisible();
        await expect(dialog.getByLabel('Email address')).toBeFocused();

        // Tab through more stops than the dialog has: focus never reaches the page behind it.
        // (A modal <dialog> lets Tab pass to the browser's own toolbar between rounds, so "nothing
        // focused" is allowed; anything outside the dialog is not.)
        const visited = new Set<string>();
        for (let i = 0; i < 10; i++) {
            await page.keyboard.press('Tab');
            const where = await page.evaluate(() => {
                const el = document.activeElement;
                if (!el || el === document.body) return 'none';
                return el.closest('dialog') ? `dialog:${el.getAttribute('aria-label') ?? el.textContent?.trim() ?? el.tagName}` : `outside:${el.outerHTML.slice(0, 80)}`;
            });
            expect(where).not.toMatch(/^outside:/);
            visited.add(where);
        }
        // It went round: the email field, Close, Cancel... were all reached.
        expect([...visited].filter(v => v.startsWith('dialog:')).length).toBeGreaterThanOrEqual(3);

        await page.keyboard.press('Escape');
        await expect(dialog).toBeHidden();
        await expect(opener).toBeFocused();
    });

    test('a dialog cannot be closed while its request is running', async ({ page, api }) => {
        const invite = api.hold('POST', /^\/Teams\/1\/members$/);
        await api.install(page);
        await page.goto('/team');

        await page.getByRole('button', { name: 'Invite player' }).click();
        const dialog = page.getByRole('dialog', { name: 'Invite player' });
        await dialog.getByLabel('Email address').fill('new@example.com');
        await dialog.getByRole('button', { name: 'Send invite' }).click();
        await invite.requested;

        await expect(dialog.getByRole('button', { name: 'Sending...' })).toBeVisible();
        await page.keyboard.press('Escape');
        await expect(dialog.getByRole('button', { name: 'Close' })).toBeDisabled();
        await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeDisabled();
        await expect(dialog).toBeVisible();

        invite.release();
        await expect(dialog).toBeHidden();
        await expect(page.getByText('Invite sent to new@example.com.')).toBeVisible();
        expect(api.requestsTo('POST', /^\/Teams\/1\/members$/)).toHaveLength(1);
    });

    test('pressing submit twice while saving sends one request', async ({ page, api }) => {
        const create = api.hold('POST', /^\/Matches\/1$/);
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Add match' }).first().click();
        const dialog = page.getByRole('dialog', { name: 'Add match' });
        await dialog.getByLabel('Opponent').fill('Kyoto Kitsune');
        await dialog.getByLabel('Date and time').fill('2026-11-20T19:00');
        await dialog.getByRole('button', { name: 'Add match' }).click();
        await create.requested;
        await dialog.getByRole('button', { name: 'Saving...' }).click({ force: true });
        await page.keyboard.press('Enter');

        create.release();
        await expect(dialog).toBeHidden();
        expect(api.requestsTo('POST', /^\/Matches\/1$/)).toHaveLength(1);
    });

    test('the Add Match dialog starts on the opponent field', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/dashboard');

        await page.getByRole('button', { name: 'Add match' }).click();

        await expect(page.getByRole('dialog', { name: 'Add match' }).getByLabel('Opponent')).toBeFocused();
    });

    test('a confirmation starts on Cancel, so a stray Enter does nothing', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/team');

        await page.getByRole('button', { name: 'Delete team' }).click();
        const dialog = page.getByRole('dialog', { name: 'Delete team' });
        // Delete asks for the team name, so focus starts in that field instead.
        await expect(dialog.getByLabel('Type Tokyo Ronin to continue')).toBeFocused();
        await expect(dialog.getByRole('button', { name: 'Delete team' })).toBeDisabled();
        await page.keyboard.press('Escape');

        await page.getByRole('button', { name: 'Actions for kenji' }).click();
        await page.getByRole('menuitem', { name: 'Kick player' }).click();
        const kick = page.getByRole('dialog', { name: 'Kick player' });
        await expect(kick.getByRole('button', { name: 'Cancel' })).toBeFocused();
        await page.keyboard.press('Enter');
        await expect(kick).toBeHidden();
        expect(api.requestsTo('DELETE', /^\/Teams\/1\/members\//)).toHaveLength(0);
    });

    test('a dialog opened after a failed one starts clean', async ({ page, api }) => {
        api.fail('DELETE', /^\/Teams\/1\/members\/kenji$/, 400, 'The team changed while removing this player. Refresh and try again.');
        api.fail('POST', /^\/Teams\/1\/members$/, 404, 'A user with this email does not exist. They must register first.');
        await api.install(page);
        await page.goto('/team');

        // A failed kick shows its error in the kick dialog only.
        await page.getByRole('button', { name: 'Actions for kenji' }).click();
        await page.getByRole('menuitem', { name: 'Kick player' }).click();
        const kick = page.getByRole('dialog', { name: 'Kick player' });
        await kick.getByRole('button', { name: 'Kick player' }).click();
        await expect(kick.getByRole('alert')).toHaveText('The team changed while removing this player. Refresh and try again.');
        await kick.getByRole('button', { name: 'Cancel' }).click();

        await page.getByRole('button', { name: 'Actions for mika' }).click();
        await page.getByRole('menuitem', { name: 'Change role' }).click();
        const role = page.getByRole('dialog', { name: 'Change role: mika' });
        await expect(role).toBeVisible();
        await expect(role.getByRole('alert')).toHaveCount(0);
        await role.getByRole('button', { name: 'Cancel' }).click();

        // The invite dialog forgets the email and the error from the last time it was open.
        await page.getByRole('button', { name: 'Invite player' }).click();
        const invite = page.getByRole('dialog', { name: 'Invite player' });
        await invite.getByLabel('Email address').fill('ghost@example.com');
        await invite.getByRole('button', { name: 'Send invite' }).click();
        await expect(invite.getByRole('alert')).toHaveText('A user with this email does not exist. They must register first.');
        await invite.getByRole('button', { name: 'Cancel' }).click();

        await page.getByRole('button', { name: 'Invite player' }).click();
        await expect(invite.getByLabel('Email address')).toHaveValue('');
        await expect(invite.getByRole('alert')).toHaveCount(0);
    });
});
