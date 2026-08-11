/**
 * ボタンをローディング表示に切り替えて非同期処理を実行する
 */
export async function withButtonLoading(button, action, options) {
    const originalHtml = button.innerHTML;
    const originalDisabled = button.disabled;
    const baseLabel = button.textContent?.trim() || '処理';
    const busyText = options?.busyText ?? `${baseLabel}中...`;
    button.disabled = true;
    button.setAttribute('aria-busy', 'true');
    button.innerHTML = `<span class="icon"><i class="fas fa-spinner fa-spin" aria-hidden="true"></i></span><span>${busyText}</span>`;
    try {
        return await action();
    }
    finally {
        button.innerHTML = originalHtml;
        button.disabled = originalDisabled;
        button.removeAttribute('aria-busy');
    }
}
/**
 * 画面中央のローディングオーバーレイ表示を切り替える
 */
export function setOverlayLoading(overlay, isLoading, options) {
    const message = overlay.querySelector('[data-loading-message]');
    if (message && options?.busyText) {
        message.textContent = options.busyText;
    }
    overlay.classList.toggle('is-active', isLoading);
    overlay.setAttribute('aria-hidden', isLoading ? 'false' : 'true');
    if (isLoading) {
        overlay.setAttribute('aria-busy', 'true');
    }
    else {
        overlay.removeAttribute('aria-busy');
    }
}
//# sourceMappingURL=loading.js.map