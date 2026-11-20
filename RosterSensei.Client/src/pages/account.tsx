import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { ExternalLink, Gamepad2, LogIn, ShieldAlert, ShieldCheck, Unlink } from 'lucide-react';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { type SteamAccount, type SteamSignInStarted } from '../types';
import PageHeader, { PageContainer } from '../components/page-header';
import { Alert, Avatar, Badge, Button, Card, CardHeader, ConfirmDialog, ErrorState, LoadingRegion, Skeleton, useToast } from '../components/ui';

const loadSteamAccount: ResourceLoader<SteamAccount> = signal =>
    api.get<SteamAccount>('/Account/steam', { signal }).then(response => response.data);

/** The openid.* fields Steam added to the URL when it sent the user back, or null when it didn't. */
function readSteamReply(params: URLSearchParams): Record<string, string> | null {
    if (!params.has('openid.mode')) return null;
    const reply: Record<string, string> = {};
    params.forEach((value, key) => {
        if (key.startsWith('openid.')) reply[key] = value;
    });
    return reply;
}

export default function Account() {
    return (
        <PageContainer>
            <PageHeader title="Account" description="Your own settings. They apply in every team you're in." />
            <SteamAccountCard />
        </PageContainer>
    );
}

function SteamAccountCard() {
    const toast = useToast();
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const { data, error, isLoading, isFetching, reload } = useResource(loadSteamAccount, 'Failed to load your Steam account.');

    // The result of the last sign-in or unlink. It wins over the loaded data, so a GET that was still
    // in flight when the change was saved can't put the old state back on screen.
    const [saved, setSaved] = useState<SteamAccount | null>(null);
    const account = saved ?? data;
    const isLinked = !!account?.steamId;

    const [isUnlinkOpen, setIsUnlinkOpen] = useState(false);
    const signIn = useAction();
    const complete = useAction();
    const unlink = useAction();

    // Coming back from Steam: finish the sign-in once. The ref survives StrictMode's second effect run,
    // and the reply is single-use on the server, so sending it twice would only show a false error.
    const handledReply = useRef(false);
    useEffect(() => {
        const reply = readSteamReply(searchParams);
        if (!reply || handledReply.current) return;
        handledReply.current = true;
        // Clean the address bar right away, so a reload or a shared link doesn't resend the reply.
        navigate('/account', { replace: true });

        void complete
            .run(() => api.post<SteamAccount>('/Account/steam/sign-in/complete', { parameters: reply }), 'Failed to link your Steam account.')
            .then(result => {
                if (!result.ok) return;
                setSaved(result.data.data);
                toast.success('Steam account linked.');
            });
        // Runs for the URL Steam returned to; the other values are stable.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [searchParams]);

    const handleSignIn = async () => {
        const returnUrl = `${window.location.origin}/account`;
        const result = await signIn.run(
            () => api.post<SteamSignInStarted>('/Account/steam/sign-in', { returnUrl }),
            'Failed to start Steam sign-in.',
        );
        // Leaves the app for steamcommunity.com; Steam sends the user back to returnUrl.
        if (result.ok) window.location.assign(result.data.data.redirectUrl);
    };

    const handleUnlink = async () => {
        const result = await unlink.run(() => api.delete('/Account/steam'), 'Failed to unlink your Steam account.');
        if (!result.ok) return;
        setSaved({ steamId: null, profileUrl: null, verified: false });
        setIsUnlinkOpen(false);
        toast.success('Steam account unlinked.');
    };

    const signInLabel = !isLinked ? 'Sign in through Steam' : account?.verified ? 'Use another Steam account' : 'Verify with Steam';

    return (
        <Card>
            <CardHeader
                title="Steam account"
                description="Sign in through Steam so your team sees your Steam name and profile on the roster. Roster Sensei never sees your Steam password."
            />
            <div className="border-t border-line px-5 py-4">
                {isLoading || complete.isRunning ? (
                    <LoadingRegion label={complete.isRunning ? 'Finishing Steam sign-in' : 'Loading your Steam account'} className="space-y-2">
                        <Skeleton className="h-4 w-1/3" />
                        <Skeleton className="h-11 w-full" />
                    </LoadingRegion>
                ) : error && !account ? (
                    <ErrorState compact title="Couldn't load your Steam account" message={error} onRetry={reload} retrying={isFetching} />
                ) : (
                    <div className="flex flex-col gap-4">
                        {complete.error && <Alert tone="danger">{complete.error}</Alert>}
                        {signIn.error && <Alert tone="danger">{signIn.error}</Alert>}

                        {isLinked && account ? (
                            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                <div className="flex min-w-0 items-center gap-3">
                                    {account.avatarUrl ? (
                                        <Avatar name={account.personaName ?? 'Steam'} imageUrl={account.avatarUrl} />
                                    ) : (
                                        <span aria-hidden="true" className="flex size-10 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent-text">
                                            <Gamepad2 className="size-5" />
                                        </span>
                                    )}
                                    <div className="min-w-0">
                                        <p className="flex min-w-0 flex-wrap items-center gap-2">
                                            <span className="truncate font-semibold text-fg">{account.personaName || 'Linked'}</span>
                                            {account.verified ? (
                                                <Badge tone="success" icon={<ShieldCheck />}>Verified</Badge>
                                            ) : (
                                                <Badge tone="warning" icon={<ShieldAlert />}>Unverified</Badge>
                                            )}
                                        </p>
                                        <p className="truncate text-sm text-fg-subtle tabular-nums">SteamID64 <span className="text-fg-muted">{account.steamId}</span></p>
                                    </div>
                                </div>
                                <div className="flex shrink-0 flex-wrap gap-2">
                                    {account.profileUrl && (
                                        <a
                                            href={account.profileUrl}
                                            target="_blank"
                                            rel="noopener noreferrer"
                                            className="inline-flex h-9 items-center gap-1.5 rounded-md border border-line-strong px-3 text-sm font-semibold text-fg hover:bg-hover pointer-coarse:h-11"
                                        >
                                            <ExternalLink className="size-4" aria-hidden="true" />
                                            Steam profile
                                            <span className="sr-only">(opens in a new tab)</span>
                                        </a>
                                    )}
                                    <Button
                                        size="sm"
                                        variant="ghost"
                                        onClick={() => { unlink.reset(); setIsUnlinkOpen(true); }}
                                        leftIcon={<Unlink className="size-4" aria-hidden="true" />}
                                    >
                                        Unlink
                                    </Button>
                                </div>
                            </div>
                        ) : null}

                        {isLinked && account && !account.verified && (
                            <Alert tone="warning">
                                This Steam account was added by pasting its ID, so it isn't proven to be yours. Sign in through Steam to verify it.
                            </Alert>
                        )}

                        <div>
                            <Button
                                variant={isLinked && account?.verified ? 'secondary' : 'primary'}
                                size={isLinked && account?.verified ? 'sm' : undefined}
                                onClick={handleSignIn}
                                loading={signIn.isRunning}
                                loadingText="Opening Steam..."
                                leftIcon={<LogIn className="size-4" aria-hidden="true" />}
                            >
                                {signInLabel}
                            </Button>
                        </div>
                    </div>
                )}
            </div>

            <ConfirmDialog
                open={isUnlinkOpen}
                onClose={() => setIsUnlinkOpen(false)}
                onConfirm={handleUnlink}
                title="Unlink Steam account"
                confirmLabel="Unlink"
                busyLabel="Unlinking..."
                busy={unlink.isRunning}
                error={unlink.error}
            >
                Your Steam profile will no longer show on your teams' rosters. You can sign in through Steam again at any time.
            </ConfirmDialog>
        </Card>
    );
}
