export const persistedPlayerStateKey = 'radicorder-player-state';

export type PersistedPlayerState = {
    sourceUrl: string;
    sourceToken?: string | null;
    title?: string | null;
    recordId?: string | null;
    currentTime?: number;
    playbackRate?: number;
    wasPlaying?: boolean;
    savedAtUtc: string;
};

export function readPersistedPlayerState(key: string = persistedPlayerStateKey): PersistedPlayerState | null {
    const raw = sessionStorage.getItem(key);
    if (!raw) {
        return null;
    }

    try {
        const parsed = JSON.parse(raw) as PersistedPlayerState;
        if (!parsed?.sourceUrl) {
            return null;
        }

        return parsed;
    } catch {
        return null;
    }
}

export function writePersistedPlayerState(state: PersistedPlayerState, key: string = persistedPlayerStateKey): void {
    sessionStorage.setItem(key, JSON.stringify(state));
}

export function clearPersistedPlayerState(key: string = persistedPlayerStateKey): void {
    sessionStorage.removeItem(key);
}
