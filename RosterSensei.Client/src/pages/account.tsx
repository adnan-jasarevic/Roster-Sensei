import { useState, type FormEvent } from 'react';
import { ExternalLink, Gamepad2, Link2, Unlink } from 'lucide-react';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { useResource, type ResourceLoader } from '../hooks/use-resource';
import { type SteamAccount } from '../types';
import PageHeader, { PageContainer } from '../components/page-header';
import { Alert, Button, Card, CardHeader, ConfirmDialog, ErrorState, Field, Input, LoadingRegion, Skeleton, useToast } from '../components/ui';

// Same wording as SteamId.FormatHint in the API.
const FORMAT_HINT = 'Your SteamID64 (17 digits), your steamcommunity.com/profiles/ URL, or a STEAM_0:X:Y or [U:1:N] ID.';

const loadSteamAccount: ResourceLoader<SteamAccount> = signal =>
    api.get<SteamAccount>('/Account/steam', { signal }).then(response => response.data);

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
    const { data, error, isLoading, isFetching, reload } = useResource(loadSteamAccount, 'Failed to load your Steam account.');

    // The result of the last link or unlink. It wins over the loaded data, so a GET that was still
    // in flight when the change was saved can't put the old state back on screen.
    const [saved, setSaved] = useState<SteamAccount | null>(null);
    const account = saved ?? data;
    const isLinked = !!account?.steamId;

    // The change form and the unlink confirmation are independent: the confirmation opens over the
    // form, and cancelling it leaves the form as it was. So these stay two flags, not one state.
    const [isEditing, setIsEditing] = useState(false);
    const [isUnlinkOpen, setIsUnlinkOpen] = useState(false);

    const [input, setInput] = useState('');
    // A problem with what was typed, shown on the field. Any other failure is `link.error`.
    const [fieldError, setFieldError] = useState('');
    const link = useAction();
    const unlink = useAction();

    const showForm = !isLinked || isEditing;

    const startEditing = () => {
        setInput('');
        setFieldError('');
        link.reset();
        setIsEditing(true);
    };

    const handleLink = async (event: FormEvent) => {
        event.preventDefault();
        const value = input.trim();
        if (!value) return;

        setFieldError('');
        const result = await link.run(() => api.put<SteamAccount>('/Account/steam', { steam: value }), 'Failed to link your Steam account.');
        if (result.ok) {
            setSaved(result.data.data);
            setIsEditing(false);
            setInput('');
            toast.success('Steam account linked.');
            return;
        }
        // A 400 is about what was typed, so it goes on the field. A 409 means another user has it.
        if (result.httpStatus === 400) {
            setFieldError(result.message);
            link.reset();
        }
    };

    const handleUnlink = async () => {
        const result = await unlink.run(() => api.delete('/Account/steam'), 'Failed to unlink your Steam account.');
        if (!result.ok) return;
        setSaved({ steamId: null, profileUrl: null });
        setIsUnlinkOpen(false);
        setIsEditing(false);
        toast.success('Steam account unlinked.');
    };

    return (
        <Card>
            <CardHeader
                title="Steam account"
                description="Link your Steam account so your team can open your Steam profile from the roster."
            />
            <div className="border-t border-line px-5 py-4">
                {isLoading ? (
                    <LoadingRegion label="Loading your Steam account" className="space-y-2">
                        <Skeleton className="h-4 w-1/3" />
                        <Skeleton className="h-11 w-full" />
                    </LoadingRegion>
                ) : error && !account ? (
                    <ErrorState compact title="Couldn't load your Steam account" message={error} onRetry={reload} retrying={isFetching} />
                ) : (
                    <div className="flex flex-col gap-4">
                        {isLinked && account && (
                            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                                <div className="flex min-w-0 items-center gap-3">
                                    <span aria-hidden="true" className="flex size-10 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent-text">
                                        <Gamepad2 className="size-5" />
                                    </span>
                                    <div className="min-w-0">
                                        <p className="font-semibold text-fg">Linked</p>
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
                                    {!isEditing && (
                                        <Button size="sm" variant="secondary" onClick={startEditing}>Change</Button>
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
                        )}

                        {showForm && (
                            <form onSubmit={handleLink} className="flex flex-col gap-3" aria-label={isLinked ? 'Change Steam account' : 'Link Steam account'}>
                                {link.error && <Alert tone="danger">{link.error}</Alert>}
                                <Field label={isLinked ? 'New Steam profile or ID' : 'Steam profile or ID'} hint={FORMAT_HINT} error={fieldError || undefined}>
                                    <Input
                                        value={input}
                                        onChange={e => { setInput(e.target.value); setFieldError(''); }}
                                        placeholder="https://steamcommunity.com/profiles/7656119..."
                                        autoComplete="off"
                                        spellCheck={false}
                                        maxLength={200}
                                        autoFocus={isEditing}
                                    />
                                </Field>
                                <div className="flex flex-wrap gap-2">
                                    <Button type="submit" disabled={!input.trim()} loading={link.isRunning} loadingText="Linking..." leftIcon={<Link2 className="size-4" aria-hidden="true" />}>
                                        {isLinked ? 'Save Steam account' : 'Link Steam account'}
                                    </Button>
                                    {isEditing && (
                                        <Button variant="ghost" onClick={() => setIsEditing(false)} disabled={link.isRunning}>Cancel</Button>
                                    )}
                                </div>
                            </form>
                        )}
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
                Your Steam profile will no longer show on your teams' rosters. You can link it again at any time.
            </ConfirmDialog>
        </Card>
    );
}
