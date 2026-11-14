import { useId } from 'react';
import { cx } from '../../lib/cx';

interface LogoMarkProps {
    /** Width and height, in pixels or a CSS length such as "100%". */
    size?: number | string;
    className?: string;
}

/** Seal-script strokes of R and S, carved out of the stamp. Shared with public/favicon.svg. */
const SEAL_CUTS = (
    <>
        <path d="M29 50h8.5L48 82h-8.5z" />
        <path d="M19 20h28v30H19zm7 7v16h14V27z" fillRule="evenodd" />
        <rect x="19" y="20" width="7" height="62" />
        <path d="M53 20h28v7H60v16h21v39H53V66h7v9h14V50H53z" />
    </>
);

/**
 * A hanko (name seal): R and S in square seal script, carved out of a red stamp. The cuts and the
 * worn border are transparent, so the mark sits on any surface in either theme.
 */
export function LogoMark({ size = 32, className }: LogoMarkProps) {
    const maskId = useId();
    return (
        <svg width={size} height={size} viewBox="0 0 100 100" className={className} aria-hidden="true">
            <mask id={maskId}>
                <rect width="100" height="100" fill="#fff" />
                <g fill="#000">{SEAL_CUTS}</g>
                <rect x="10.5" y="10.5" width="79" height="79" rx="3" fill="none" stroke="#000" strokeWidth="2.2" />
            </mask>
            <rect x="6" y="6" width="88" height="88" rx="9" fill="var(--rs-accent)" mask={`url(#${maskId})`} />
        </svg>
    );
}

/**
 * The login page's backdrop: a plain sun disc whose lower half is cut into roster lines. It's
 * deliberately a plain disc: the rayed sun has a painful history in much of Asia, where a lot of
 * CS2 is played, so never add rays to it.
 */
export function SunDisc({ size = 32, className }: LogoMarkProps) {
    const maskId = useId();
    return (
        <svg width={size} height={size} viewBox="0 0 32 32" className={className} aria-hidden="true">
            <mask id={maskId}>
                <rect width="32" height="32" fill="#fff" />
                <rect y="17" width="32" height="1.5" fill="#000" />
                <rect y="21" width="32" height="2" fill="#000" />
                <rect y="25.5" width="32" height="2.5" fill="#000" />
            </mask>
            <circle cx="16" cy="16" r="15" fill="var(--rs-accent)" mask={`url(#${maskId})`} />
        </svg>
    );
}

interface LogoProps {
    size?: 'sm' | 'md';
    className?: string;
}

export function Logo({ size = 'md', className }: LogoProps) {
    return (
        <span className={cx('inline-flex items-center gap-2.5', className)}>
            <LogoMark size={size === 'sm' ? 28 : 34} />
            <span className={cx('font-display font-black tracking-tight text-fg leading-none', size === 'sm' ? 'text-base' : 'text-xl')}>
                Roster <span className="text-accent-text">Sensei</span>
            </span>
        </span>
    );
}
