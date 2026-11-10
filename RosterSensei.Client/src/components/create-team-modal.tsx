import { useState, type FormEvent } from 'react';
import api from '../services/api';
import { useAction } from '../hooks/use-action';
import { type Team } from '../types';
import { Alert, Button, Field, Input, Modal } from './ui';

interface CreateTeamModalProps {
    onClose: () => void;
    onCreated: (team: Team) => void;
}

export default function CreateTeamModal({ onClose, onCreated }: CreateTeamModalProps) {
    const [name, setName] = useState('');
    const create = useAction();
    const isCreating = create.isRunning;

    const handleSubmit = async (e: FormEvent) => {
        e.preventDefault();
        // Fails with, for example, the 3-team limit, or a name with characters that aren't allowed.
        const result = await create.run(() => api.post<Team>('/Teams', { name: name.trim() }), 'Failed to create the team. Please try again.');
        if (!result.ok) return;
        onCreated(result.data.data);
        onClose();
    };

    return (
        <Modal
            open
            onClose={onClose}
            title="Create a new team"
            size="sm"
            busy={isCreating}
            closeOnBackdrop={false}
            footer={
                <>
                    <Button variant="ghost" onClick={onClose} disabled={isCreating}>Cancel</Button>
                    <Button type="submit" form="create-team-form" disabled={!name.trim()} loading={isCreating} loadingText="Creating...">Create team</Button>
                </>
            }
        >
            <form id="create-team-form" onSubmit={handleSubmit} className="flex flex-col gap-4">
                {create.error && <Alert tone="danger">{create.error}</Alert>}
                <Field label="Team name">
                    <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Tokyo Ronin" maxLength={50} autoComplete="off" data-autofocus />
                </Field>
            </form>
        </Modal>
    );
}
