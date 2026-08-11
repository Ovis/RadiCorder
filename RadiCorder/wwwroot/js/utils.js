/**
 * textContent指定
 * @param element
 * @param selector
 * @param text
 */
export const setTextContent = (element, selector, text) => {
    const target = element.querySelector(selector);
    if (target)
        target.textContent = text;
};
/**
 * innerHtml指定
 * @param element
 * @param selector
 * @param html
 */
export const setInnerHtml = (element, selector, html) => {
    const target = element.querySelector(selector);
    if (target)
        target.innerHTML = html;
};
/**
 * 許可タグのみ残す簡易サニタイズ
 * - 未許可タグは中身を残して展開
 * - 未許可属性は削除
 */
export const sanitizeHtml = (input) => {
    if (!input)
        return '';
    const allowedTags = new Set([
        'br', 'b', 'strong', 'i', 'em', 'u',
        'p', 'ul', 'ol', 'li', 'span',
        'code', 'pre', 'small', 'sup', 'sub',
        'a'
    ]);
    const allowedAttrsByTag = {
        a: new Set(['href', 'title', 'target', 'rel'])
    };
    const doc = new DOMParser().parseFromString(input, 'text/html');
    const sanitizeNode = (node) => {
        if (node.nodeType === Node.ELEMENT_NODE) {
            const el = node;
            const tag = el.tagName.toLowerCase();
            if (!allowedTags.has(tag)) {
                const parent = el.parentNode;
                if (parent) {
                    while (el.firstChild) {
                        parent.insertBefore(el.firstChild, el);
                    }
                    parent.removeChild(el);
                    return;
                }
            }
            else {
                const allowedAttrs = allowedAttrsByTag[tag] ?? new Set();
                Array.from(el.attributes).forEach(attr => {
                    if (!allowedAttrs.has(attr.name.toLowerCase())) {
                        el.removeAttribute(attr.name);
                    }
                });
                if (tag === 'a') {
                    const href = el.getAttribute('href') ?? '';
                    let safe = false;
                    try {
                        const url = new URL(href, window.location.origin);
                        safe = url.protocol === 'http:' || url.protocol === 'https:' || url.protocol === 'mailto:';
                    }
                    catch {
                        safe = false;
                    }
                    if (!safe) {
                        el.removeAttribute('href');
                    }
                    const target = (el.getAttribute('target') ?? '').toLowerCase();
                    if (target === '_blank') {
                        el.setAttribute('rel', 'noopener noreferrer');
                    }
                }
            }
        }
        else if (node.nodeType === Node.COMMENT_NODE) {
            node.parentNode?.removeChild(node);
            return;
        }
        Array.from(node.childNodes).forEach(child => sanitizeNode(child));
    };
    Array.from(doc.body.childNodes).forEach(child => sanitizeNode(child));
    return doc.body.innerHTML;
};
/**
 * HTMLエスケープ（テキストのみを安全に埋め込みたい場合）
 */
export const escapeHtml = (input) => {
    return (input ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
};
/**
 * Attribute指定
 * @param element
 * @param selector
 * @param attribute
 * @param value
 */
export const setAttribute = (element, selector, attribute, value) => {
    const target = element.querySelector(selector);
    if (target)
        target.setAttribute(attribute, value);
};
/**
 * イベント指定
 * @param element
 * @param selector
 * @param event
 * @param callback
 */
export const setEventListener = (element, selector, event, callback) => {
    const target = element.querySelector(selector);
    if (target)
        target.addEventListener(event, callback);
};
/**
 * Date型の値を表示用の文字列に変換
 * @param date
 * @returns
 */
export function formatDisplayDateTime(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    return `${year}/${month}/${day} ${hours}:${minutes}`;
}
/**
 * タイムゾーン指定のないUTC文字列をUTCとしてDateへ変換する
 */
export function parseUtcDateTime(value) {
    if (!value) {
        return null;
    }
    const trimmed = value.trim();
    if (!trimmed) {
        return null;
    }
    const normalized = trimmed.includes('T')
        ? trimmed
        : trimmed.replace(' ', 'T');
    const hasTimeZone = /[zZ]|[+-]\d{2}:\d{2}$/.test(normalized);
    const parsed = new Date(hasTimeZone ? normalized : `${normalized}Z`);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
}
//# sourceMappingURL=utils.js.map