export function createInlineToast(containerId, messageId, durationMs = 2500) {
    let timerId;
    return (message, isSuccess = true) => {
        const container = document.getElementById(containerId);
        const messageElm = document.getElementById(messageId);
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
export function wireInlineToastClose(buttonId, containerId) {
    const button = document.getElementById(buttonId);
    if (!button) {
        return;
    }
    button.addEventListener('click', () => {
        const container = document.getElementById(containerId);
        container?.classList.remove('is-active');
    });
}
//# sourceMappingURL=inline-toast.js.map