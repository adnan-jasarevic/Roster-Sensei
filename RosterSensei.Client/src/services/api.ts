import axios from 'axios';
import { getToken, signOut } from './session';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'https://localhost:7185/api';

const api = axios.create({
    baseURL: API_BASE_URL,
    headers: {
        'Content-Type': 'application/json',
    },
});

api.interceptors.request.use((config) => {
    const token = getToken();
    if (token && config.headers) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

// An expired or invalid token: clear it and send the user back to the login page.
// The login request itself is skipped so wrong credentials still show their own error.
api.interceptors.response.use(
    (response) => response,
    (error) => {
        const isLoginRequest = error.config?.url?.includes('/Auth/login');
        if (error.response?.status === 401 && !isLoginRequest) {
            signOut();
            if (window.location.pathname !== '/login') {
                window.location.assign('/login');
            }
        }
        return Promise.reject(error);
    }
);

export default api;
