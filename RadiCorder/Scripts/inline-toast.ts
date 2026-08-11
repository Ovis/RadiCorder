type InlineToastPresenter = (message: string, isSuccess?: boolean) => void;

export function createInlineToast(containerId: string, messageId: string, durationMs = 2500): InlineToastPresenter {
    let timerId: number | undefined;

    return (message: string, isSuccess = true) => {
        const container = document.getElementById(containerId) as HTMLDivElement | null;
        const messageElm = document.getElementById(messageId) as HTMLSpanElement | null;
        if (!container || !messageElm) {
            return;
        }

        messageElm.textContent = message;
        container.classList.toggle('is-error', !isSuccess);
        container.classList.add('is-active');

        if (timerId !== undefined) {
            window.clearTimeout(timerId);
        }

        timerId = window.setTimeout(() => {
            container.classList.remove('is-active');
        }, durationMs);
    };
}

export function wireInlineToastClose(buttonId: string, containerId: string): void {
    const button = document.getElementById(buttonId) as HTMLButtonElement | null;
    if (!button) {
        return;
    }

    button.addEventListener('click', () => {
        const container = document.getElementById(containerId) as HTMLDivElement | null;
        container?.classList.remove('is-active');
    });
}
