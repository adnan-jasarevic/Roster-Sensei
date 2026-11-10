import { Navigate } from 'react-router-dom';
import React from 'react';
import { isSignedIn } from '../services/session';

interface RouteGuardProps {
    children: React.ReactNode;
}

/** Pages for signed-in users. Anyone else is sent to the login page. */
export default function PrivateRoute({ children }: RouteGuardProps) {
    if (!isSignedIn()) {
        return <Navigate to="/login" replace />;
    }

    return <>{children}</>;
}

/**
 * The login and register pages. Someone who is already signed in goes straight to the dashboard
 * instead of being asked to log in again. If their token has expired, the first API call there
 * signs them out and brings them back to the login page.
 */
export function SignedOutRoute({ children }: RouteGuardProps) {
    if (isSignedIn()) {
        return <Navigate to="/dashboard" replace />;
    }

    return <>{children}</>;
}
