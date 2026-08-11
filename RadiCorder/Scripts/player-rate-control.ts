export const playerPlaybackRateOptions = [1, 1.25, 1.5, 1.75, 2];

type RateDirection = 1 | -1;

type BindPlaybackRateControlOptions = {
    button: HTMLButtonElement;
    audioElm: HTMLAudioElement;
    playbackRateOptions?: number[];
    onChanged?: () => void;
};

export function getPlayerPlaybackRate(audioElm: HTMLAudioElement, playbackRateOptions: number[] = playerPlaybackRateOptions): number {
    return normalizePlaybackRate(audioElm.playbackRate, playbackRateOptions);
}

export function normalizePlaybackRate(rate: number, playbackRateOptions: number[] = playerPlaybackRateOptions): number {
    if (!Number.isFinite(rate)) {
        return playbackRateOptions[0];
    }

    if (playbackRateOptions.includes(rate)) {
        return rate;
    }

    return playbackRateOptions.reduce((closest, current) => {
        const closestDistance = Math.abs(closest - rate);
        const currentDistance = Math.abs(current - rate);
        return currentDistance < closestDistance ? current : closest;
    }, playbackRateOptions[0]);
}

export function formatPlaybackRateLabel(rate: number): string {
    return `速度 ${rate}x`;
}

export function applyPlaybackRate(audioElm: HTMLAudioElement, rate: number, playbackRateOptions: number[] = playerPlaybackRateOptions): void {
    const normalizedRate = normalizePlaybackRate(rate, playbackRateOptions);
    audioElm.playbackRate = normalizedRate;
    audioElm.defaultPlaybackRate = normalizedRate;
    // ブラウザ実装差で ratechange が発火しないケースを吸収し、UIラベルを同期する
    audioElm.dispatchEvent(new Event('ratechange'));
}

export function resetPlaybackRate(audioElm: HTMLAudioElement, playbackRateOptions: number[] = playerPlaybackRateOptions): void {
    applyPlaybackRate(audioElm, playbackRateOptions[0]);
}

export function setPlaybackRateButtonLabel(button: HTMLButtonElement, audioElm: HTMLAudioElement, playbackRateOptions: number[] = playerPlaybackRateOptions): void {
    const rateLabel = formatPlaybackRateLabel(getPlayerPlaybackRate(audioElm, playbackRateOptions));
    button.textContent = rateLabel;
    button.setAttribute('aria-label', `再生速度切替 (現在 ${rateLabel})`);
}

export function bindPlaybackRateControl(options: BindPlaybackRateControlOptions): void {
    const playbackRateValues = options.playbackRateOptions ?? playerPlaybackRateOptions;
    const { button, audioElm, onChanged } = options;

    let rateLongPressTimer: number | null = null;
    let rateLongPressTriggered = false;
    let suppressContextMenuOnce = false;
    let suppressTouchContextMenuUntil = 0;

    const changeRate = (direction: RateDirection): void => {
        const current = getPlayerPlaybackRate(audioElm, playbackRateValues);
        const currentIndex = playbackRateValues.indexOf(current);
        const nextIndex = (currentIndex + direction + playbackRateValues.length) % playbackRateValues.length;
        const next = playbackRateValues[nextIndex];
        applyPlaybackRate(audioElm, next);
        onChanged?.();
    };

    button.addEventListener('click', () => {
        if (rateLongPressTriggered) {
            rateLongPressTriggered = false;
            return;
        }

        changeRate(1);
    });

    button.addEventListener('contextmenu', (event) => {
        event.preventDefault();
        if (Date.now() < suppressTouchContextMenuUntil) {
            return;
        }

        if (suppressContextMenuOnce) {
            suppressContextMenuOnce = false;
            return;
        }

        changeRate(-1);
    });

    button.addEventListener('pointerdown', (event) => {
        if (event.pointerType !== 'touch') {
            return;
        }

        // タッチ由来の contextmenu は右クリック用途とは分離して無効化する
        suppressTouchContextMenuUntil = Date.now() + 2000;

        if (rateLongPressTimer !== null) {
            window.clearTimeout(rateLongPressTimer);
        }

        rateLongPressTimer = window.setTimeout(() => {
            rateLongPressTriggered = true;
            // 同一長押し操作で後続 contextmenu が来ても二重変更しない
            suppressContextMenuOnce = true;
            changeRate(-1);
            rateLongPressTimer = null;
        }, 450);
    });

    button.addEventListener('pointerup', () => {
        if (rateLongPressTimer !== null) {
            window.clearTimeout(rateLongPressTimer);
            rateLongPressTimer = null;
        }
    });

    button.addEventListener('pointercancel', () => {
        if (rateLongPressTimer !== null) {
            window.clearTimeout(rateLongPressTimer);
            rateLongPressTimer = null;
        }
    });
}
