import { Link, useNavigate } from 'react-router-dom';
import React, { useState } from 'react';
import axios from 'axios';
import api from '../services/api';
import { getApiErrorMessage } from '../services/errors';
import { signIn } from '../services/session';
import { useAction } from '../hooks/use-action';
import { type LoginResponse } from '../types';
import AuthLayout from '../components/auth-layout';
import { Alert, Button, Field, Input, PasswordInput } from '../components/ui';

const loginErrorMessage = (err: unknown) => {
    if (axios.isAxiosError(err) && !err.response) return "Can't reach the server. Check your connection and try again.";
    if (axios.isAxiosError(err) && err.response && err.response.status >= 500) return 'Something went wrong on our side. Please try again.';
    return getApiErrorMessage(err, 'Wrong email or password. Try again.');
};

export default function Login() {
    const navigate = useNavigate();

    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const login = useAction();

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        const result = await login.run(async () => {
            const response = await api.post<LoginResponse>('/Auth/login', { email, password });
            signIn(response.data.token);
        }, loginErrorMessage);
        if (result.ok) navigate('/dashboard');
    };

    return (
        <AuthLayout
            title="Welcome back"
            subtitle="Log in to get back to your roster."
            footer={<>Don't have an account? <Link to="/register" className="font-semibold text-accent-text underline-offset-4 hover:underline">Register</Link></>}
        >
            <form onSubmit={handleLogin} className="flex flex-col gap-4">
                {login.error && <Alert tone="danger">{login.error}</Alert>}

                <Field label="Email">
                    <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="you@example.com" autoComplete="email" required />
                </Field>

                <Field label="Password">
                    <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
                </Field>

                <Button type="submit" size="lg" fullWidth loading={login.isRunning} loadingText="Logging in..." className="mt-2">
                    Log in
                </Button>
            </form>
        </AuthLayout>
    );
}
