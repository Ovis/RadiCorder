let globalToastTimerId;
/**
 * 全画面共通のトースト表示
 */
export function showGlobalToast(message, isSuccess = true) {
    let toast = document.getElementById('global-result-toast');
    let messageElm = document.getElementById('global-result-toast-message');
    let closeButton = document.getElementById('global-result-toast-close');
    if (!toast) {
        toast = document.createElement('div');
        toast.id = 'global-result-toast';
        toast.className = 'rk-toast';
        toast.setAttribute('role', 'status');
        toast.setAttribute('aria-live', 'polite');
        toast.setAttribute('aria-atomic', 'true');
        toast.innerHTML = `
            <div class="rk-toast-body">
                <span id="global-result-toast-message"></span>
                <button class="delete" aria-label="close" id="global-result-toast-close"></button>
            </div>
        `;
        document.body.appendChild(toast);
        messageElm = document.getElementById('global-result-toast-message');
        closeButton = document.getElementById('global-result-toast-close');
    }
    if (!toast || !messageElm) {
        return;
    }
    messageElm.textContent = message;
    toast.classList.toggle('is-error', !isSuccess);
    toast.classList.add('is-active');
    if (closeButton && !closeButton.dataset.bound) {
        closeButton.addEventListener('click', () => {
            toast?.classList.remove('is-active');
        });
        closeButton.dataset.bound = 'true';
    }
    if (globalToastTimerId !== undefined) {
        window.clearTimeout(globalToastTimerId);
    }
    globalToastTimerId = window.setTimeout(() => {
        toast?.classList.remove('is-active');
    }, 2500);
}
/**
 * 全画面共通の確認ダイアログ表示
 */
export function showConfirmDialog(message, options) {
    return new Promise((resolve) => {
        const existing = document.getElementById('global-confirm-modal');
        if (existing) {
            existing.remove();
        }
        const modal = document.createElement('div');
        modal.className = 'modal is-active';
        modal.id = 'global-confirm-modal';
        const modalBackground = document.createElement('div');
        modalBackground.className = 'modal-background';
        const modalCard = document.createElement('div');
        modalCard.className = 'modal-card';
        modalCard.style.maxWidth = '28rem';
        const modalHeader = document.createElement('header');
        modalHeader.className = 'modal-card-head';
        const modalTitle = document.createElement('p');
        modalTitle.className = 'modal-card-title';
        modalTitle.textContent = options?.title ?? '確認';
        const closeButton = document.createElement('button');
        closeButton.className = 'delete';
        closeButton.setAttribute('aria-label', 'close');
        modalHeader.appendChild(modalTitle);
        modalHeader.appendChild(closeButton);
        const modalBody = document.createElement('section');
        modalBody.className = 'modal-card-body';
        const modalMessage = document.createElement('p');
        modalMessage.style.whiteSpace = 'pre-line';
        modalMessage.textContent = message;
        modalBody.appendChild(modalMessage);
        const modalFooter = document.createElement('footer');
        modalFooter.className = 'modal-card-foot';
        const buttons = document.createElement('div');
        buttons.className = 'buttons';
        const okButton = document.createElement('button');
        okButton.className = 'button is-danger';
        okButton.dataset.action = 'ok';
        okButton.textContent = options?.okText ?? 'OK';
        const cancelButton = document.createElement('button');
        cancelButton.className = 'button is-light';
        cancelButton.dataset.action = 'cancel';
        cancelButton.textContent = options?.cancelText ?? 'キャンセル';
        buttons.appendChild(okButton);
        buttons.appendChild(cancelButton);
        modalFooter.appendChild(buttons);
        modalCard.appendChild(modalHeader);
        modalCard.appendChild(modalBody);
        modalCard.appendChild(modalFooter);
        modal.appendChild(modalBackground);
        modal.appendChild(modalCard);
        const close = (result) => {
            modal.remove();
            resolve(result);
        };
        modalBackground.addEventListener('click', () => close(false));
        closeButton.addEventListener('click', () => close(false));
        cancelButton.addEventListener('click', () => close(false));
        okButton.addEventListener('click', () => close(true));
        document.body.appendChild(modal);
    });
}
//# sourceMappingURL=feedback.js.map