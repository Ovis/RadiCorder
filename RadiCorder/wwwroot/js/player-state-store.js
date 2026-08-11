export const persistedPlayerStateKey = 'radicorder-player-state';
export function readPersistedPlayerState(key = persistedPlayerStateKey) {
    const raw = sessionStorage.getItem(key);
    if (!raw) {
        return null;
    }
    try {
        const parsed = JSON.parse(raw);
        if (!parsed?.sourceUrl) {
            return null;
        }
        return parsed;
    }
    catch {
        return null;
    }
}
export function writePersistedPlayerState(state, key = persistedPlayerStateKey) {
    sessionStorage.setItem(key, JSON.stringify(state));
}
export function clearPersistedPlayerState(key = persistedPlayerStateKey) {
    sessionStorage.removeItem(key);
}
//# sourceMappingURL=player-state-store.js.map