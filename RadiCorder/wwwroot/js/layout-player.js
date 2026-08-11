import { readPersistedPlayerState, writePersistedPlayerState, clearPersistedPlayerState } from './player-state-store.js';
import { createStandardPlayerJumpControls } from './player-jump-controls.js';
import { applyPlaybackRate, playerPlaybackRateOptions } from './player-rate-control.js';
const resumeWindowMs = 15 * 60 * 1000;
const defaultDocumentTitle = document.title;
let currentState = null;
let currentHls = null;
function updateDocumentTitle(title) {
    const normalized = title?.trim();
    document.title = normalized ? `${normalized} - RadiCorder` : defaultDocumentTitle;
}
async function ensureHlsScriptLoaded() {
    const hlsWindow = window;
    if (hlsWindow.Hls) {
        return true;
    }
    const existing = document.getElementById('global-hls-script');
    if (existing) {
        await new Promise((resolve, reject) => {
            if (hlsWindow.Hls) {
                resolve();
                return;
            }
            existing.addEventListener('load', () => resolve(), { once: true });
            existing.addEventListener('error', () => reject(new Error('hls.js load error')), { once: true });
        }).catch(() => undefined);
        return !!hlsWindow.Hls;
    }
    const script = document.createElement('script');
    script.id = 'global-hls-script';
    script.src = '/lib/hls.js/hls.min.js';
    document.head.appendChild(script);
    await new Promise((resolve, reject) => {
        script.addEventListener('load', () => resolve(), { once: true });
        script.addEventListener('error', () => reject(new Error('hls.js load error')), { once: true });
    }).catch(() => undefined);
    return !!hlsWindow.Hls;
}
function createPlayerShell() {
    const footer = document.getElementById('audio-player');
    if (!footer) {
        return null;
    }
    footer.innerHTML = '';
    const playerContainerElm = document.createElement('div');
    playerContainerElm.className = 'player-container';
    const playerMainRowElm = document.createElement('div');
    playerMainRowElm.className = 'player-main-row';
    const audioPlayerElm = document.createElement('audio');
    audioPlayerElm.id = 'audio-player-elm';
    audioPlayerElm.style.width = '100%';
    audioPlayerElm.style.height = '2rem';
    audioPlayerElm.controls = true;
    const closeButton = document.createElement('button');
    closeButton.type = 'button';
    closeButton.className = 'player-close-button';
    closeButton.setAttribute('aria-label', 'プレイヤーを閉じる');
    closeButton.innerHTML = '<i class="fas fa-xmark" aria-hidden="true"></i>';
    closeButton.addEventListener('click', () => {
        if (currentHls) {
            currentHls.destroy();
            currentHls = null;
        }
        audioPlayerElm.pause();
        audioPlayerElm.removeAttribute('src');
        audioPlayerElm.load();
        footer.innerHTML = '';
        currentState = null;
        clearPersistedPlayerState();
        updateDocumentTitle(null);
    });
    playerMainRowElm.appendChild(audioPlayerElm);
    playerMainRowElm.appendChild(closeButton);
    playerContainerElm.appendChild(playerMainRowElm);
    playerContainerElm.appendChild(createStandardPlayerJumpControls(audioPlayerElm));
    footer.appendChild(playerContainerElm);
    return audioPlayerElm;
}
function wirePersist(audioElm) {
    const persist = () => {
        if (!currentState) {
            return;
        }
        writePersistedPlayerState({
            ...currentState,
            currentTime: Number.isFinite(audioElm.currentTime) ? audioElm.currentTime : 0,
            playbackRate: Number.isFinite(audioElm.playbackRate) ? audioElm.playbackRate : 1,
            wasPlaying: !audioElm.paused && !audioElm.ended,
            savedAtUtc: new Date().toISOString()
        });
    };
    audioElm.addEventListener('timeupdate', persist);
    audioElm.addEventListener('pause', persist);
    audioElm.addEventListener('ratechange', persist);
    window.addEventListener('beforeunload', persist);
}
async function tryRestorePlayer() {
    const resumeEnabledFlag = document.body.dataset.resumePlaybackAcrossPages;
    const resumeEnabled = resumeEnabledFlag !== 'false';
    if (!resumeEnabled) {
        clearPersistedPlayerState();
        updateDocumentTitle(null);
        return;
    }
    if (document.getElementById('audio-player-elm')) {
        return;
    }
    const state = readPersistedPlayerState();
    if (!state) {
        return;
    }
    // ブラウザ再起動/新規タブでは復帰しない（同一オリジン内の遷移時のみ復帰）
    const referrer = document.referrer ?? '';
    const isInternalReferrer = referrer.length > 0 && referrer.startsWith(window.location.origin);
    if (!isInternalReferrer) {
        clearPersistedPlayerState();
        updateDocumentTitle(null);
        return;
    }
    const savedAt = new Date(state.savedAtUtc).getTime();
    if (!Number.isFinite(savedAt) || Date.now() - savedAt > resumeWindowMs) {
        clearPersistedPlayerState();
        return;
    }
    const audioElm = createPlayerShell();
    if (!audioElm) {
        return;
    }
    currentState = state;
    updateDocumentTitle(state.title);
    const shouldResumePlaying = state.wasPlaying !== false;
    applyPlaybackRate(audioElm, Number.isFinite(state.playbackRate ?? NaN) ? state.playbackRate : 1, playerPlaybackRateOptions);
    const canNativeHls = !!audioElm.canPlayType('application/vnd.apple.mpegurl');
    const canUseHlsJs = await ensureHlsScriptLoaded();
    if (canUseHlsJs) {
        const hlsConstructor = window.Hls;
        if (!hlsConstructor) {
            clearPersistedPlayerState();
            updateDocumentTitle(null);
            return;
        }
        const hls = new hlsConstructor();
        if (state.sourceToken) {
            hls.config.xhrSetup = (xhr) => {
                xhr.setRequestHeader('X-Radiko-AuthToken', state.sourceToken ?? '');
            };
        }
        currentHls = hls;
        hls.loadSource(state.sourceUrl);
        hls.attachMedia(audioElm);
        hls.on(hlsConstructor.Events.MANIFEST_PARSED, () => {
            if (Number.isFinite(state.currentTime ?? NaN) && (state.currentTime ?? 0) > 0) {
                audioElm.currentTime = state.currentTime;
            }
            if (shouldResumePlaying) {
                void audioElm.play();
            }
        });
    }
    else if (canNativeHls) {
        audioElm.src = state.sourceUrl;
        audioElm.onloadedmetadata = () => {
            if (Number.isFinite(state.currentTime ?? NaN) && (state.currentTime ?? 0) > 0) {
                audioElm.currentTime = state.currentTime;
            }
            if (shouldResumePlaying) {
                void audioElm.play();
            }
        };
    }
    else {
        clearPersistedPlayerState();
        updateDocumentTitle(null);
        return;
    }
    wirePersist(audioElm);
}
document.addEventListener('DOMContentLoaded', async () => {
    await tryRestorePlayer();
});
//# sourceMappingURL=layout-player.js.map