import { createStandardPlayerJumpControls, disposePlayerJumpControls } from './player-jump-controls.js';
import { applyPlaybackRate, playerPlaybackRateOptions } from './player-rate-control.js';
import { clearPersistedPlayerState, readPersistedPlayerState, writePersistedPlayerState } from './player-state-store.js';
import { showGlobalToast } from './feedback.js';
const resumeWindowMs = 15 * 60 * 1000;
let defaultDocumentTitle = document.title;
let options = {};
let currentState = null;
let currentHls = null;
let generation = 0;
function reportError(message) {
    (options.onError ?? ((text) => showGlobalToast(text, false)))(message);
}
function setTitle(title) {
    const normalized = title?.trim();
    document.title = normalized ? `${normalized} - RadiCorder` : defaultDocumentTitle;
}
export function setPlayerPageTitle(title) {
    defaultDocumentTitle = title;
    setTitle(currentState?.title);
}
function persist() {
    const audio = getPlayerAudio();
    if (!currentState || !audio) {
        return;
    }
    currentState = {
        ...currentState,
        currentTime: Number.isFinite(audio.currentTime) ? audio.currentTime : 0,
        playbackRate: Number.isFinite(audio.playbackRate) ? audio.playbackRate : 1,
        wasPlaying: !audio.paused && !audio.ended,
        savedAtUtc: new Date().toISOString()
    };
    writePersistedPlayerState(currentState);
}
function createPlayer() {
    const footer = document.getElementById('audio-player');
    if (!footer) {
        reportError('プレイヤーの初期化に失敗しました。');
        return null;
    }
    footer.innerHTML = '';
    const container = document.createElement('div');
    container.className = 'player-container';
    const row = document.createElement('div');
    row.className = 'player-main-row';
    const audio = document.createElement('audio');
    audio.id = 'audio-player-elm';
    audio.style.width = '100%';
    audio.style.height = '2rem';
    audio.controls = true;
    audio.addEventListener('timeupdate', persist);
    audio.addEventListener('seeked', persist);
    audio.addEventListener('play', persist);
    audio.addEventListener('pause', persist);
    audio.addEventListener('ratechange', persist);
    audio.addEventListener('ended', () => {
        persist();
        if (options.onEnded) {
            options.onEnded();
        }
        else {
            finishPlayer();
        }
    });
    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'player-close-button';
    close.setAttribute('aria-label', 'プレイヤーを閉じる');
    close.innerHTML = '<i class="fas fa-xmark" aria-hidden="true"></i>';
    close.addEventListener('click', () => stopPlayer());
    row.append(audio, close);
    container.append(row);
    footer.append(container);
    return audio;
}
export function configurePlayer(next) {
    options = next;
    const audio = getPlayerAudio();
    if (audio) {
        updateControls(audio);
        options.onStateChanged?.(currentState);
    }
}
function updateControls(audio) {
    const container = audio.closest('.player-container');
    const previous = container?.querySelector('.player-jump-controls');
    if (previous) {
        disposePlayerJumpControls(previous);
        previous.remove();
    }
    const controls = options.createControls
        ? options.createControls(audio, currentState)
        : createStandardPlayerJumpControls(audio);
    container?.appendChild(controls);
}
export function getPlayerAudio() {
    return document.getElementById('audio-player-elm');
}
export function getPlayerHls() {
    return currentHls;
}
export async function resumePlayer() {
    const audio = getPlayerAudio();
    if (audio) {
        await resumeAudio(audio, generation);
    }
}
export function stopPlayer() {
    generation++;
    const audio = getPlayerAudio();
    audio?.pause();
    currentHls?.destroy();
    currentHls = null;
    if (audio) {
        audio.removeAttribute('src');
        audio.load();
    }
    document.getElementById('audio-player')?.replaceChildren();
    currentState = null;
    clearPersistedPlayerState();
    setTitle(null);
    options.onStateChanged?.(null);
}
export function finishPlayer() {
    currentHls?.destroy();
    currentHls = null;
    currentState = null;
    clearPersistedPlayerState();
    setTitle(null);
    options.onStateChanged?.(null);
}
async function resumeAudio(audio, requestId) {
    if (requestId !== generation) {
        return;
    }
    try {
        await audio.play();
    }
    catch (error) {
        if (requestId !== generation) {
            return;
        }
        persist();
        reportError(error instanceof DOMException && error.name === 'NotAllowedError'
            ? 'ブラウザが自動再生を許可しませんでした。再生ボタンを押してください。'
            : '再生に失敗しました。再生ボタンを押して再試行してください。');
    }
}
export async function playPlayerSource(source, isRestore = false) {
    const audio = getPlayerAudio() ?? createPlayer();
    if (!audio) {
        return false;
    }
    const hlsConstructor = window.Hls;
    const canUseHls = hlsConstructor?.isSupported?.() === true;
    const canUseNativeHls = !!audio.canPlayType('application/vnd.apple.mpegurl');
    if (!canUseHls && !canUseNativeHls) {
        reportError('このブラウザはHLS再生に対応していません。');
        return false;
    }
    const previous = currentState;
    const sameSource = previous?.sourceUrl === source.sourceUrl &&
        (previous.sourceToken ?? '') === (source.sourceToken ?? '');
    const rate = isRestore || sameSource ? source.playbackRate ?? 1 : playerPlaybackRateOptions[0];
    const requestId = ++generation;
    currentState = null;
    currentHls?.destroy();
    currentHls = null;
    audio.removeAttribute('src');
    audio.load();
    applyPlaybackRate(audio, rate, playerPlaybackRateOptions);
    currentState = {
        ...source,
        currentTime: source.currentTime ?? 0,
        playbackRate: rate,
        wasPlaying: source.wasPlaying !== false,
        savedAtUtc: new Date().toISOString()
    };
    setTitle(source.title);
    writePersistedPlayerState(currentState);
    updateControls(audio);
    options.onStateChanged?.(currentState);
    const start = () => {
        if (requestId !== generation) {
            return;
        }
        const position = source.currentTime ?? 0;
        if (Number.isFinite(position) && position > 0) {
            try {
                audio.currentTime = position;
            }
            catch {
                // ライブ配信など、保存位置が現在の再生可能範囲外の場合は既定位置から再生する。
            }
        }
        if (source.wasPlaying !== false) {
            void resumeAudio(audio, requestId);
        }
    };
    if (canUseHls && hlsConstructor) {
        const hls = new hlsConstructor();
        if (source.sourceToken) {
            hls.config.xhrSetup = (xhr) => {
                xhr.setRequestHeader('X-Radiko-AuthToken', source.sourceToken ?? '');
            };
        }
        currentHls = hls;
        hls.on(hlsConstructor.Events.MANIFEST_PARSED, start);
        hls.on(hlsConstructor.Events.ERROR, (_event, data) => {
            if (requestId === generation && data?.fatal) {
                reportError('ストリームの読み込みに失敗しました。再生をやり直してください。');
            }
        });
        hls.loadSource(source.sourceUrl);
        hls.attachMedia(audio);
    }
    else {
        audio.addEventListener('loadedmetadata', start, { once: true });
        audio.src = source.sourceUrl;
    }
    return true;
}
export async function restorePlayer() {
    if (document.body.dataset.resumePlaybackAcrossPages === 'false') {
        clearPersistedPlayerState();
        return;
    }
    if (getPlayerAudio()) {
        return;
    }
    const state = readPersistedPlayerState();
    if (!state) {
        return;
    }
    let internalReferrer = false;
    try {
        internalReferrer = new URL(document.referrer).origin === window.location.origin;
    }
    catch {
        // referrer が取得できない場合は新規タブと同様に扱う。
    }
    const savedAt = new Date(state.savedAtUtc).getTime();
    if (!internalReferrer ||
        !Number.isFinite(savedAt) || savedAt > Date.now() || Date.now() - savedAt > resumeWindowMs) {
        clearPersistedPlayerState();
        return;
    }
    await playPlayerSource(state, true);
}
window.addEventListener('pagehide', persist);
//# sourceMappingURL=player-controller.js.map