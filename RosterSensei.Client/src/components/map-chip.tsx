import { cx } from '../lib/cx';
import type { MatchMap } from '../types';

// "7-5, 6-6 +OT" style summary of a map's halves, or empty when halves weren't recorded.
const halvesText = (map: MatchMap) => {
    if (map.ourFirstHalf == null || map.opponentFirstHalf == null || map.ourSecondHalf == null || map.opponentSecondHalf == null)
        return '';
    const regulation = map.ourFirstHalf + map.ourSecondHalf + map.opponentFirstHalf + map.opponentSecondHalf;
    const total = (map.ourScore ?? 0) + (map.opponentScore ?? 0);
    const overtime = total > regulation ? ' +OT' : '';
    return `${map.ourFirstHalf}-${map.opponentFirstHalf}, ${map.ourSecondHalf}-${map.opponentSecondHalf}${overtime}`;
};

/** A map of a match, with its score and halves once it's been played. */
export function MapChip({ map }: { map: MatchMap }) {
    const played = map.ourScore != null && map.opponentScore != null;
    const halves = halvesText(map);
    const scoreClass = !played || map.ourScore === map.opponentScore
        ? 'text-fg-muted'
        : map.ourScore! > map.opponentScore! ? 'text-success' : 'text-danger';

    return (
        <li className="rounded border border-line bg-sunken px-2 py-1 text-xs text-fg-muted">
            {map.mapName}
            {played && <span className={cx('ml-1.5 font-bold tabular-nums', scoreClass)}>{map.ourScore}-{map.opponentScore}</span>}
            {halves && <span className="ml-1.5 text-fg-subtle tabular-nums">({halves})</span>}
        </li>
    );
}
