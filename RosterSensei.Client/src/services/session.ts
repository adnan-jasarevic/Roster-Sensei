// The signed-in session: the one place that knows where the JWT is kept.
//
// States: signed out (no token) and signed in (a token is stored). The token can also be expired,
// which only the API can tell: its 401 moves the session to signed out (see api.ts).
//
//   signed out --signIn--> signed in --signOut, or a 401 from the API--> signed out

const TOKEN_KEY = 'jwt_token';

/** The stored token, or null when signed out. */
export function getToken(): string | null {
    try {
        return localStorage.getItem(TOKEN_KEY);
    } catch {
        // Storage is blocked: nothing can be kept, so the user is signed out.
        return null;
    }
}

export const isSignedIn = () => getToken() !== null;

export function signIn(token: string) {
    localStorage.setItem(TOKEN_KEY, token);
}

export function signOut() {
    try {
        localStorage.removeItem(TOKEN_KEY);
    } catch {
        // Nothing was stored in the first place.
    }
}
