import type { Page } from '@playwright/test';
import { test, expect } from './support/fixtures';
import { finishedMatch, member, team, type MockApi } from './support/mock-api';
import type { DemoImportPreview, DemoSummary, Match } from '../src/types';

// The real reader runs Go compiled to WebAssembly in a worker. These tests swap the worker for a
// stand-in that answers with a fixed scoreboard, so they check the dialog and what it sends.
const DEMO: Omit<DemoSummary, 'parserVersion'> = {
    patchVersion: 14175,
    fingerprint: 'f'.repeat(64),
    mapName: 'de_mirage',
    rounds: 24,
    sides: [
        {
            score: 7, firstHalf: 4, secondHalf: 3, coaches: [],
            players: [{ steamId: '76561198339617378', name: 'forkyz', kills: 16, deaths: 19, assists: 6, damage: 2154, headshotKills: 13 }],
        },
        {
            score: 17, firstHalf: 8, secondHalf: 9,
            coaches: [{ steamId: '76561197995658520', name: 'Kairi' }],
            players: [
                { steamId: '76561198212412773', name: 'Maze', kills: 23, deaths: 13, assists: 11, damage: 2588, headshotKills: 13 },
                { steamId: '76561198182462587', name: 'szarkira', kills: 16, deaths: 12, assists: 7, damage: 1808, headshotKills: 11 },
            ],
        },
    ],
};

const PREVIEW: DemoImportPreview = {
    ourSide: 1,
    mapOrder: 1,
    replacesImport: false,
    sides: [
        { score: 7, firstHalf: 4, secondHalf: 3, coaches: [], players: [{ steamId: '76561198339617378', name: 'forkyz', kills: 16, deaths: 19, assists: 6, adr: 89.8, headshotPercentage: 81.3, userId: null, username: null, canPlay: true }] },
        {
            score: 17, firstHalf: 8, secondHalf: 9,
            coaches: [{ steamId: '76561197995658520', name: 'Kairi', userId: null, username: null }],
            players: [
                { steamId: '76561198212412773', name: 'Maze', kills: 23, deaths: 13, assists: 11, adr: 107.8, headshotPercentage: 56.5, userId: 'kenji', username: 'kenji', canPlay: true },
                { steamId: '76561198182462587', name: 'szarkira', kills: 16, deaths: 12, assists: 7, adr: 75.3, headshotPercentage: 68.8, userId: null, username: null, canPlay: true },
            ],
        },
    ],
    coaches: [{ userId: null, displayName: 'Kairi', steamId: '76561197995658520' }],
};

async function fakeReader(page: Page, outcome: { summary?: typeof DEMO; failure?: { message: string; patchVersion: number | null } }) {
    const script = `
        self.onmessage = () => {
            self.postMessage({ type: 'progress', value: 0.5 });
            const outcome = ${JSON.stringify(outcome)};
            if (outcome.summary) self.postMessage({ type: 'done', summary: outcome.summary });
            else self.postMessage({ type: 'failed', ...outcome.failure });
        };`;
    await page.route('**/demo-reader/worker.js', route => route.fulfill({ contentType: 'text/javascript', body: script }));
}

function withMatch(api: MockApi) {
    const t = team(1, 'Tokyo Ronin', 'Captain', true, { matches: [finishedMatch(7, 'Friends', 13, 9, '2026-10-20T18:00:00Z')] });
    t.members.push(member('ren', 'Player'));
    api.teams = [t];
    api.on('GET', /^\/DemoImport\/status/, () => ({ json: { paused: false, message: null } }));
    api.on('POST', /^\/DemoImport\/1\/7\/preview$/, () => ({ json: PREVIEW }));
    return t;
}

async function openImport(page: Page) {
    await page.goto('/matches');
    await page.getByRole('button', { name: 'Details of the match vs Friends' }).click();
    await page.getByRole('dialog', { name: 'vs Friends' }).getByRole('button', { name: 'Import demo' }).click();
    const dialog = page.getByRole('dialog', { name: 'Import demo vs Friends' });
    await dialog.getByLabel('Demo file').setInputFiles({ name: 'scrim.dem', mimeType: 'application/octet-stream', buffer: Buffer.from('PBDEMS2') });
    return dialog;
}

test.describe('Demo import', () => {
    test('reads a demo, links players and coaches, and saves the stats', async ({ page, api }) => {
        const t = withMatch(api);
        const saved: Match = { ...t.matches[0], coaches: [{ userId: null, displayName: 'Guest', steamId: null }], importedMapOrders: [1], version: 2 };
        api.on('POST', /^\/DemoImport\/1\/7$/, () => ({ json: saved }));
        await fakeReader(page, { summary: DEMO });
        await api.install(page);

        const dialog = await openImport(page);

        await expect(dialog.getByText('Mirage', { exact: true })).toBeVisible();
        await expect(dialog.getByText('17 - 7')).toBeVisible();
        await expect(dialog.getByText('halves 8-4 and 9-3', { exact: false })).toBeVisible();
        await expect(dialog.getByRole('row', { name: /Maze/ })).toContainText('107.8');
        await expect(dialog.getByRole('list', { name: 'Coaches of this match' })).toContainText('Kairi');

        // szarkira hasn't linked Steam: picked by hand.
        await dialog.getByRole('combobox', { name: 'Team member for szarkira' }).click();
        await page.getByRole('option', { name: 'ren' }).click();
        await dialog.getByRole('button', { name: 'Remove coach Kairi' }).click();
        await dialog.getByLabel('Name of a coach from outside the team').fill('Guest');
        await dialog.getByRole('button', { name: 'Add coach' }).click();

        await dialog.getByRole('button', { name: 'Import stats' }).click();
        await expect(dialog).toBeHidden();
        await expect(page.getByText('Demo stats saved for the match vs Friends.')).toBeVisible();

        const [request] = api.requestsTo('POST', /^\/DemoImport\/1\/7$/);
        expect(request.body).toEqual({
            demo: { ...DEMO, parserVersion: 'demoinfocs-golang 5.3.0' },
            ourSide: 1,
            mapOrder: 1,
            players: [
                { steamId: '76561198212412773', userId: 'kenji' },
                { steamId: '76561198182462587', userId: 'ren' },
            ],
            coaches: [{ name: 'Guest' }],
            version: 1,
        });
        // The demo file itself was never sent.
        expect(api.requests.every(r => !JSON.stringify(r.body ?? '').includes('PBDEMS2'))).toBe(true);
    });

    test('says so and reads nothing while import is paused', async ({ page, api }) => {
        withMatch(api);
        api.on('GET', /^\/DemoImport\/status/, () => ({ json: { paused: true, message: 'Demo import is paused after a CS2 update.' } }));
        await fakeReader(page, { summary: DEMO });
        await api.install(page);

        const dialog = await openImport(page);

        await expect(dialog.getByText('Demo import is paused after a CS2 update.')).toBeVisible();
        expect(api.requestsTo('POST', /preview$/)).toHaveLength(0);
    });

    test('reports a demo it cannot read, which can pause import after a game update', async ({ page, api }) => {
        withMatch(api);
        api.on('POST', /^\/DemoImport\/1\/failures$/, () => ({ json: { paused: true, message: 'Demo import is paused after a CS2 update.' } }));
        await fakeReader(page, { failure: { message: 'unknown entity class', patchVersion: 14300 } });
        await api.install(page);

        const dialog = await openImport(page);

        await expect(dialog.getByText('Demo import is paused after a CS2 update.')).toBeVisible();
        expect(api.requestsTo('POST', /^\/DemoImport\/1\/failures$/).map(r => r.body)).toEqual([
            { parserVersion: 'demoinfocs-golang 5.3.0', patchVersion: 14300 },
        ]);
    });

    test('shows the coaches of a match', async ({ page, api }) => {
        const t = withMatch(api);
        t.matches[0].coaches = [{ userId: 'mika', displayName: 'mika', steamId: null }, { userId: null, displayName: 'Kairi', steamId: '76561197995658520' }];
        await api.install(page);

        await page.goto('/matches');
        await page.getByRole('button', { name: 'Details of the match vs Friends' }).click();

        await expect(page.getByRole('dialog', { name: 'vs Friends' }).getByText('Coaches: mika, Kairi')).toBeVisible();
    });
});
