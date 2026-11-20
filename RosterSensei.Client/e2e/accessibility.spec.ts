import AxeBuilder from '@axe-core/playwright';
import type { Page } from '@playwright/test';
import { test, expect, hasHorizontalScroll } from './support/fixtures';
import { finishedMatch, team, type MockApi } from './support/mock-api';

const PAGES = [
    { path: '/login', heading: 'Welcome back', signedIn: false },
    { path: '/register', heading: 'Create your account', signedIn: false },
    { path: '/dashboard', heading: 'Dashboard', signedIn: true },
    { path: '/team', heading: 'Team roster', signedIn: true },
    { path: '/calendar', heading: 'Calendar', signedIn: true },
    { path: '/matches', heading: 'Matches', signedIn: true },
    { path: '/map-pool', heading: 'Map Pool', signedIn: true },
    { path: '/statistics', heading: 'Statistics', signedIn: true },
    { path: '/account', heading: 'Account', signedIn: true },
];

function withData(api: MockApi) {
    const withLineup = finishedMatch(1, 'Kyoto Kitsune', 13, 9, '2026-10-20T18:00:00Z');
    withLineup.lineup = [
        { userId: 'kenji', username: 'kenji', role: 'AWP', played: true, rating: 1.21, kills: 22, deaths: 14, assists: 3, adr: 88.4, headshotPercentage: 41.2 },
        { userId: 'mika', username: 'mika', role: null, played: false, rating: null },
    ];
    const tokyo = team(1, 'Tokyo Ronin', 'Captain', true, {
        matches: [withLineup, finishedMatch(2, 'Osaka Oni', 7, 13, '2026-10-27T18:00:00Z')],
        mapRatings: [
            { userId: 'kenji', side: 'T', mapName: 'Mirage', rating: 6.5 },
            { userId: 'me', side: 'CT', mapName: 'Nuke', rating: 8 },
        ],
    });
    tokyo.members[1].steamProfileUrl = 'https://steamcommunity.com/profiles/76561198000000001';
    api.teams = [tokyo];
    api.steamId = '76561198000000002';
}

async function open(page: Page, api: MockApi, path: string, signedIn: boolean, heading: string) {
    await api.install(page, { signedIn });
    await page.goto(path);
    await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible();
    // Wait for skeletons to be replaced and entrance animations to finish.
    await expect(page.getByRole('status').filter({ hasText: /^Loading/ })).toHaveCount(0);
    await page.waitForTimeout(400);
}

/** Dialogs of the match with a scoreboard, opened from its row on the Matches page. */
const MATCH_DIALOGS = [
    { name: 'Match details', button: 'Details of the match vs Kyoto Kitsune', dialog: 'vs Kyoto Kitsune' },
    { name: 'Lineup', button: 'Edit lineup for the match vs Kyoto Kitsune', dialog: 'Lineup vs Kyoto Kitsune' },
];

async function openMatchDialog(page: Page, api: MockApi, d: (typeof MATCH_DIALOGS)[number]) {
    await open(page, api, '/matches', true, 'Matches');
    await page.getByRole('button', { name: d.button }).click();
    const dialog = page.getByRole('dialog', { name: d.dialog, exact: true });
    await expect(dialog).toBeVisible();
    // The roster loads inside the Lineup dialog.
    await expect(dialog.getByRole('status').filter({ hasText: /^Loading/ })).toHaveCount(0);
    await page.waitForTimeout(300);
    return dialog;
}

for (const theme of ['dark', 'light'] as const) {
    test.describe(`accessibility scan, ${theme} theme`, () => {
        for (const p of PAGES) {
            test(p.path, async ({ page, api }) => {
                withData(api);
                await page.addInitScript(t => localStorage.setItem('rs-theme', t), theme);
                await open(page, api, p.path, p.signedIn, p.heading);
                expect(await page.evaluate(() => document.documentElement.dataset.theme)).toBe(theme);

                const results = await new AxeBuilder({ page })
                    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
                    .analyze();

                expect(results.violations.map(v => `${v.id}: ${v.help} (${v.nodes.map(n => n.target.join(' ')).join(', ')})`)).toEqual([]);
            });
        }

        for (const d of MATCH_DIALOGS) {
            test(`the ${d.name} dialog`, async ({ page, api }) => {
                withData(api);
                await page.addInitScript(t => localStorage.setItem('rs-theme', t), theme);
                await openMatchDialog(page, api, d);

                const results = await new AxeBuilder({ page })
                    .include('dialog')
                    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
                    .analyze();

                expect(results.violations.map(v => `${v.id}: ${v.help} (${v.nodes.map(n => n.target.join(' ')).join(', ')})`)).toEqual([]);
            });
        }
    });
}

test.describe('no sideways scrolling at 320px', () => {
    test.use({ viewport: { width: 320, height: 640 } });

    for (const p of PAGES) {
        test(p.path, async ({ page, api }) => {
            withData(api);
            await open(page, api, p.path, p.signedIn, p.heading);

            expect(await hasHorizontalScroll(page)).toBe(false);
        });
    }

    test('the Add Match dialog', async ({ page, api }) => {
        withData(api);
        await open(page, api, '/matches', true, 'Matches');
        await page.getByRole('button', { name: 'Add match' }).click();
        await expect(page.getByRole('dialog', { name: 'Add match' })).toBeVisible();

        const overflows = await page.getByRole('dialog').evaluate(d => d.scrollWidth > d.clientWidth + 1 || d.getBoundingClientRect().width > window.innerWidth);
        expect(overflows).toBe(false);
    });

    for (const d of MATCH_DIALOGS) {
        test(`the ${d.name} dialog`, async ({ page, api }) => {
            withData(api);
            const dialog = await openMatchDialog(page, api, d);

            // The scoreboard table scrolls inside its own region, so the dialog itself never does.
            const overflows = await dialog.evaluate(el => el.scrollWidth > el.clientWidth + 1 || el.getBoundingClientRect().width > window.innerWidth);
            expect(overflows).toBe(false);
            expect(await hasHorizontalScroll(page)).toBe(false);
        });
    }
});
