import { CalendarClock, Check, ClipboardList, Crown, Hourglass, Minus, Shield, Star, X } from 'lucide-react';
import { type Match, type MatchOutcome, type MatchStatusName, type TeamRole, hasResult, needsResult } from '../types';
import { Badge, type BadgeTone } from './ui';

const OUTCOMES: Record<MatchOutcome, { tone: BadgeTone; icon: React.ReactNode }> = {
    Win: { tone: 'success', icon: <Check /> },
    Loss: { tone: 'danger', icon: <X /> },
    Draw: { tone: 'neutral', icon: <Minus /> },
};

export function OutcomeBadge({ outcome }: { outcome?: MatchOutcome | null }) {
    const config = outcome ? OUTCOMES[outcome] : undefined;
    if (!config) return <Badge className="w-16">{outcome || 'N/A'}</Badge>;
    return <Badge tone={config.tone} icon={config.icon} className="w-16">{outcome}</Badge>;
}

export function MatchStatusBadge({ status }: { status: MatchStatusName }) {
    if (status === 'Cancelled') return <Badge className="min-w-16 line-through">Cancelled</Badge>;
    return <Badge className="min-w-16">{status}</Badge>;
}

/** A scheduled match whose time has passed without a result being recorded. */
export function NeedsResultBadge() {
    return <Badge tone="warning" icon={<Hourglass />} className="min-w-16">Needs result</Badge>;
}

/** A scheduled match whose time hasn't come yet. */
export function UpcomingBadge() {
    return <Badge tone="accent" icon={<CalendarClock />} className="min-w-16">Upcoming</Badge>;
}

/**
 * The one tag a match gets: its result once finished, "Needs result" when its time has passed
 * without a score, "Upcoming" before that, or "Cancelled".
 */
export function MatchStateBadge({ match, now }: { match: Pick<Match, 'status' | 'scheduledDate' | 'matchOutcome'>; now: Date }) {
    if (hasResult(match)) return <OutcomeBadge outcome={match.matchOutcome} />;
    if (needsResult(match, now)) return <NeedsResultBadge />;
    if (match.status === 'Scheduled') return <UpcomingBadge />;
    return <MatchStatusBadge status={match.status} />;
}

// Every role is listed, so adding one to TeamRole won't compile until its badge is decided here.
const ROLES: Record<TeamRole, { tone: BadgeTone; icon?: React.ReactNode }> = {
    Captain: { tone: 'warning', icon: <Star /> },
    Officer: { tone: 'neutral', icon: <Shield /> },
    Player: { tone: 'neutral' },
    Coach: { tone: 'neutral', icon: <ClipboardList /> },
};

/** Ownership isn't a role: the Owner also has a role, so this badge sits next to RoleBadge. */
export function OwnerBadge() {
    return <Badge tone="accent" icon={<Crown />}>Owner</Badge>;
}

export function RoleBadge({ role }: { role: TeamRole }) {
    // The fallback is for a role from a newer API than this build knows.
    const config = ROLES[role] ?? { tone: 'neutral' as const };
    return <Badge tone={config.tone} icon={config.icon}>{role}</Badge>;
}
