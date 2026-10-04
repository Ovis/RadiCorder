import { bindPlaybackRateControl, playerPlaybackRateOptions, setPlaybackRateButtonLabel } from './player-rate-control.js';
const controlAbortControllers = new WeakMap();
export function disposePlayerJumpControls(controls) {
    controlAbortControllers.get(controls)?.abort();
    controlAbortControllers.delete(controls);
}
export const playerJumpSecondsOptions = [5, 10, 30, 60, 300];
export const playerJumpStorageKey = 'radicorder-player-jump-seconds';
export function getPlayerJumpSeconds(jumpStorageKey = playerJumpStorageKey, jumpOptions = playerJumpSecondsOptions) {
    const raw = localStorage.getItem(jumpStorageKey);
    const parsed = raw ? Number.parseInt(raw, 10) : NaN;
    if (Number.isFinite(parsed) && jumpOptions.includes(parsed)) {
        return parsed;
    }
    return jumpOptions[0];
}
export function formatJumpLabel(seconds) {
    if (seconds >= 60) {
        return `${Math.floor(seconds / 60)}分`;
    }
    return `${seconds}秒`;
}
export function seekAudio(audioElm, deltaSeconds) {
    if (!Number.isFinite(audioElm.currentTime)) {
        return;
    }
    const duration = audioElm.duration;
    let nextTime = Math.max(0, audioElm.currentTime + deltaSeconds);
    if (Number.isFinite(duration)) {
        nextTime = Math.min(nextTime, duration);
    }
    audioElm.currentTime = nextTime;
}
export function createStandardPlayerJumpControls(audioElm, options = {}) {
    const jumpOptions = options.jumpSecondsOptions ?? playerJumpSecondsOptions;
    const jumpStorageKey = options.jumpStorageKey ?? playerJumpStorageKey;
    const playbackRates = options.playbackRateOptions ?? playerPlaybackRateOptions;
    const wrapper = document.createElement('div');
    wrapper.className = 'player-jump-controls';
    const mainGroup = document.createElement('div');
    mainGroup.className = 'player-jump-main';
    const sideGroup = document.createElement('div');
    sideGroup.className = 'player-jump-side';
    const backButton = document.createElement('button');
    backButton.type = 'button';
    backButton.className = 'player-jump-button';
    const amountButton = document.createElement('button');
    amountButton.type = 'button';
    amountButton.className = 'player-jump-amount';
    const rateButton = document.createElement('button');
    rateButton.type = 'button';
    rateButton.className = 'player-jump-toggle player-speed-button';
    const forwardButton = document.createElement('button');
    forwardButton.type = 'button';
    forwardButton.className = 'player-jump-button';
    const updateLabels = () => {
        const seconds = getPlayerJumpSeconds(jumpStorageKey, jumpOptions);
        const label = formatJumpLabel(seconds);
        backButton.textContent = '-';
        backButton.setAttribute('aria-label', `${label}巻き戻し`);
        forwardButton.textContent = '+';
        forwardButton.setAttribute('aria-label', `${label}早送り`);
        amountButton.textContent = label;
        amountButton.setAttribute('aria-label', `ジャンプ量切替 (現在 ${label})`);
        setPlaybackRateButtonLabel(rateButton, audioElm, playbackRates);
        options.onUpdateLabels?.();
    };
    let jumpLongPressTimer = null;
    let jumpLongPressTriggered = false;
    let suppressJumpContextMenuOnce = false;
    let suppressTouchContextMenuUntil = 0;
    const changeJump = (direction) => {
        const current = getPlayerJumpSeconds(jumpStorageKey, jumpOptions);
        const currentIndex = jumpOptions.indexOf(current);
        const nextIndex = (currentIndex + direction + jumpOptions.length) % jumpOptions.length;
        const next = jumpOptions[nextIndex];
        localStorage.setItem(jumpStorageKey, next.toString());
        updateLabels();
    };
    backButton.addEventListener('click', () => {
        seekAudio(audioElm, -getPlayerJumpSeconds(jumpStorageKey, jumpOptions));
    });
    forwardButton.addEventListener('click', () => {
        seekAudio(audioElm, getPlayerJumpSeconds(jumpStorageKey, jumpOptions));
    });
    amountButton.addEventListener('click', () => {
        if (jumpLongPressTriggered) {
            jumpLongPressTriggered = false;
            return;
        }
        changeJump(1);
    });
    amountButton.addEventListener('contextmenu', (event) => {
        event.preventDefault();
        if (Date.now() < suppressTouchContextMenuUntil) {
            return;
        }
        if (suppressJumpContextMenuOnce) {
            suppressJumpContextMenuOnce = false;
            return;
        }
        changeJump(-1);
    });
    amountButton.addEventListener('pointerdown', (event) => {
        if (event.pointerType !== 'touch') {
            return;
        }
        // タッチ由来の contextmenu は右クリック用途とは分離して無効化する
        suppressTouchContextMenuUntil = Date.now() + 2000;
        if (jumpLongPressTimer !== null) {
            window.clearTimeout(jumpLongPressTimer);
        }
        jumpLongPressTimer = window.setTimeout(() => {
            jumpLongPressTriggered = true;
            // 同一長押し操作で後続 contextmenu が来ても二重変更しない
            suppressJumpContextMenuOnce = true;
            changeJump(-1);
            jumpLongPressTimer = null;
        }, 450);
    });
    amountButton.addEventListener('pointerup', () => {
        if (jumpLongPressTimer !== null) {
            window.clearTimeout(jumpLongPressTimer);
            jumpLongPressTimer = null;
        }
    });
    amountButton.addEventListener('pointercancel', () => {
        if (jumpLongPressTimer !== null) {
            window.clearTimeout(jumpLongPressTimer);
            jumpLongPressTimer = null;
        }
    });
    bindPlaybackRateControl({
        button: rateButton,
        audioElm,
        playbackRateOptions: playbackRates,
        onChanged: updateLabels
    });
    updateLabels();
    mainGroup.appendChild(backButton);
    mainGroup.appendChild(amountButton);
    mainGroup.appendChild(forwardButton);
    sideGroup.appendChild(rateButton);
    const extras = options.createSideButtons?.() ?? [];
    extras.forEach((element) => sideGroup.appendChild(element));
    wrapper.appendChild(mainGroup);
    wrapper.appendChild(sideGroup);
    const abortController = new AbortController();
    audioElm.addEventListener('ratechange', updateLabels, { signal: abortController.signal });
    controlAbortControllers.set(wrapper, abortController);
    return wrapper;
}
//# sourceMappingURL=player-jump-controls.js.map