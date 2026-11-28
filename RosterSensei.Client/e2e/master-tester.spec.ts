import { test, expect } from './support/fixtures';

// The master tester panel. The API decides who is a tester: 200 from GET /master-tester shows the
// panel, the 404 everyone else gets hides it.
test.describe('Master tester panel', () => {
    test('is hidden from users who are not master testers', async ({ page, api }) => {
        await api.install(page);
        await page.goto('/dashboard');

        await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
        await expect.poll(() => api.requestsTo('GET', /^\/master-tester$/).length).toBe(1);
        await expect(page.getByRole('button', { name: 'Tester' })).toHaveCount(0);
    });

    test('fills a section of the selected team and reloads the page', async ({ page, api }) => {
        api.on('GET', /^\/master-tester$/, () => ({ json: { sections: ['roster', 'matches', 'calendar', 'offdays', 'mappool'] } }));
        api.on('POST', /^\/master-tester\/teams\/1\/matches$/, () => ({ json: { teamId: 1, message: 'Added 14 results and 5 scheduled matches.' } }));
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('button', { name: 'Tester' }).click();
        const dialog = page.getByRole('dialog', { name: 'Master tester' });
        await expect(dialog).toBeVisible();

        // The tab being viewed is listed first.
        await expect(dialog.getByRole('listitem').first()).toContainText('Matches');
        await expect(dialog.getByRole('listitem').first()).toContainText('This tab');

        const loadsBefore = api.requestsTo('GET', /^\/Matches\/1$/).length;
        await dialog.getByRole('button', { name: 'Add matches' }).click();

        await expect(page.getByText('Added 14 results and 5 scheduled matches.')).toBeVisible();
        const [request] = api.requestsTo('POST', /^\/master-tester\/teams\/1\/matches$/);
        expect(request.body).toEqual({ mode: 'add', timeZone: 'Europe/Sarajevo' });
        await expect.poll(() => api.requestsTo('GET', /^\/Matches\/1$/).length).toBeGreaterThan(loadsBefore);
    });

    test('lists Availability first on the Availability page and fills it', async ({ page, api }) => {
        api.on('GET', /^\/master-tester$/, () => ({ json: { sections: ['roster', 'matches', 'calendar', 'offdays', 'availability', 'mappool'] } }));
        api.on('POST', /^\/master-tester\/teams\/1\/availability$/, () => ({ json: { teamId: 1, message: 'Made new availability for 6 made-up players.' } }));
        await api.install(page);
        await page.goto('/availability');

        await page.getByRole('button', { name: 'Tester' }).click();
        const dialog = page.getByRole('dialog', { name: 'Master tester' });
        await expect(dialog.getByRole('listitem').first()).toContainText('Availability');
        await expect(dialog.getByRole('listitem').first()).toContainText('This tab');

        await dialog.getByRole('button', { name: 'Replace availability' }).click();

        await expect(page.getByText('Made new availability for 6 made-up players.')).toBeVisible();
        const [request] = api.requestsTo('POST', /^\/master-tester\/teams\/1\/availability$/);
        expect(request.body).toEqual({ mode: 'replace', timeZone: 'Europe/Sarajevo' });
    });

    test('shows the error when generating fails', async ({ page, api }) => {
        api.on('GET', /^\/master-tester$/, () => ({ json: { sections: [] } }));
        api.fail('POST', /^\/master-tester\/teams\/1\/roster$/, 403, 'You are not in this team.');
        await api.install(page);
        await page.goto('/team');

        await page.getByRole('button', { name: 'Tester' }).click();
        const dialog = page.getByRole('dialog', { name: 'Master tester' });
        await dialog.getByRole('button', { name: 'Replace roster' }).click();

        await expect(dialog.getByText('You are not in this team.')).toBeVisible();
    });
});
