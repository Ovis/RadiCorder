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
/** 許可する書式を維持し、外部由来HTMLの入れ子や属性も検証する。 */
export const sanitizeHtml = (input) => {
    if (!input)
        return '';
    const purified = DOMPurify.sanitize(input, {
        ALLOWED_TAGS: ['br', 'b', 'strong', 'i', 'em', 'u', 'p', 'ul', 'ol', 'li', 'span', 'code', 'pre', 'small', 'sup', 'sub', 'a'],
        ALLOWED_ATTR: ['href', 'title', 'target', 'rel'],
        ALLOW_DATA_ATTR: false,
        ALLOW_ARIA_ATTR: false
    });
    const doc = new DOMParser().parseFromString(purified, 'text/html');
    doc.body.querySelectorAll('*').forEach(el => {
        if (el.tagName.toLowerCase() !== 'a') {
            Array.from(el.attributes).forEach(attr => el.removeAttribute(attr.name));
            return;
        }
        // リンクは従来どおりHTTP・HTTPS・メールと同一オリジンの相対URLに限定する。
        try {
            const url = new URL(el.getAttribute('href') ?? '', window.location.origin);
            if (!['http:', 'https:', 'mailto:'].includes(url.protocol))
                el.removeAttribute('href');
        }
        catch {
            el.removeAttribute('href');
        }
        if ((el.getAttribute('target') ?? '').toLowerCase() === '_blank')
            el.setAttribute('rel', 'noopener noreferrer');
    });
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