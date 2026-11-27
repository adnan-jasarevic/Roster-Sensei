import { MAP_POOL } from '../constants/maps';
import type { DemoSummary } from '../types';

/** Sent with every import, so a CS2 update that breaks this reader pauses only this version. */
export const DEMO_PARSER_VERSION = 'demoinfocs-golang 5.3.0';

/** The biggest file the reader accepts, packed or not. */
export const MAX_DEMO_BYTES = 1024 ** 3;

export const DEMO_FILE_TYPES = '.dem,.gz,.bz2,.zip';

export class DemoReadError extends Error {
    /** The demo's CS2 patch, when its header could be read. */
    readonly patchVersion: number | null;

    constructor(message: string, patchVersion: number | null) {
        super(message);
        this.name = 'DemoReadError';
        this.patchVersion = patchVersion;
    }
}

/**
 * Reads a demo in a Web Worker, piece by piece, and resolves with its scoreboard. The file never
 * leaves the device. The worker is ended afterwards (or when `signal` aborts), so its memory is freed.
 */
export function readDemo(file: File, onProgress: (fraction: number) => void, signal?: AbortSignal): Promise<DemoSummary> {
    if (file.size > MAX_DEMO_BYTES) {
        return Promise.reject(new DemoReadError('This file is bigger than 1 GB.', null));
    }

    return new Promise((resolve, reject) => {
        const worker = new Worker(`${import.meta.env.BASE_URL}demo-reader/worker.js`);
        const finish = () => {
            worker.terminate();
            signal?.removeEventListener('abort', abort);
        };
        const abort = () => {
            finish();
            reject(new DOMException('The demo reading was cancelled.', 'AbortError'));
        };
        if (signal?.aborted) {
            abort();
            return;
        }
        signal?.addEventListener('abort', abort);

        worker.onmessage = ({ data }) => {
            if (data.type === 'progress') {
                onProgress(data.value);
            } else if (data.type === 'done') {
                finish();
                resolve({ ...data.summary, parserVersion: DEMO_PARSER_VERSION });
            } else if (data.type === 'failed') {
                finish();
                reject(new DemoReadError(data.message, data.patchVersion ?? null));
            }
        };
        worker.onerror = event => {
            finish();
            reject(new DemoReadError(event.message || 'The demo reader could not start.', null));
        };
        worker.postMessage({ file, version: DEMO_PARSER_VERSION });
    });
}

/** "de_mirage" as the map pool spells it: "Mirage". Matches the server's spelling. */
export function demoMapName(name: string) {
    const bare = name.trim().replace(/^(de|cs|ar)_/i, '');
    return MAP_POOL.find(map => map.toLowerCase() === bare.toLowerCase()) ?? (bare.charAt(0).toUpperCase() + bare.slice(1));
}
