import { test, expect } from './support/fixtures';
import { finishedMatch, team } from './support/mock-api';
import type { Match } from '../src/types';

const scheduled = (id: number, opponentName: string, date: string): Match => ({
    id,
    opponentName,
    scheduledDate: date,
    status: 'Scheduled',
    type: 'Official',
    finalScore: '0 - 0',
    bestOf: 1,
    maps: [],
    matchOutcome: 'Draw',
    lineup: [],
});

test.describe('match filters', () => {
    test('a scheduled match whose time has passed needs a result instead of being upcoming', async ({ page, api }) => {
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, {
            matches: [
                scheduled(1, 'Future Foxes', '2026-11-20T19:00:00Z'),
                scheduled(2, 'Past Pandas', '2026-11-05T19:00:00Z'),
                finishedMatch(3, 'Kyoto Kitsune', 13, 9, '2026-10-20T18:00:00Z'),
            ],
        })];
        await page.clock.setFixedTime(new Date('2026-11-11T09:00:00Z'));
        await api.install(page);
        await page.goto('/matches');

        const rows = page.getByRole('listitem').filter({ hasText: /^.*vs / });
        await expect(rows).toHaveCount(3);
        // The badge says which is which, without picking a filter.
        await expect(rows.filter({ hasText: 'Past Pandas' })).toContainText('Needs result');
        await expect(rows.filter({ hasText: 'Future Foxes' })).toContainText('Scheduled');

        await page.getByRole('radio', { name: 'Upcoming' }).check({ force: true });
        await expect(rows).toHaveCount(1);
        await expect(rows).toContainText('Future Foxes');

        await page.getByRole('radio', { name: 'Needs result' }).check({ force: true });
        await expect(rows).toHaveCount(1);
        await expect(rows).toContainText('Past Pandas');

        await page.getByRole('radio', { name: 'Results' }).check({ force: true });
        await expect(rows).toHaveCount(1);
        await expect(rows).toContainText('Kyoto Kitsune');
    });

    test('says so when nothing is waiting for a result', async ({ page, api }) => {
        api.teams = [team(1, 'Tokyo Ronin', 'Captain', true, { matches: [finishedMatch(3, 'Kyoto Kitsune', 13, 9, '2026-10-20T18:00:00Z')] })];
        await api.install(page);
        await page.goto('/matches');

        await page.getByRole('radio', { name: 'Needs result' }).check({ force: true });
        await expect(page.getByText('Nothing is waiting for a result')).toBeVisible();
    });
});
