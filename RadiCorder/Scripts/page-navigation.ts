import { configurePlayer, setPlayerPageTitle } from './player-controller.js';

type PageCleanup = () => void;
type PageInitializer = (signal: AbortSignal) => void | PageCleanup | Promise<void | PageCleanup>;

const pages = new Map<string, PageInitializer>();
const sharedScripts = new Set(['layout.js', 'layout-player.js']);
let activeScript = getPageScript(document);
let activeCleanup: PageCleanup | null = null;
let activeAbort: AbortController | null = null;
let activationId = 0;
let request: AbortController | null = null;
let started = false;
let activeUrl = new URL(location.href);

function getPageScript(page: Document): string | null {
    const scripts = Array.from(page.querySelectorAll<HTMLScriptElement>('script[type="module"][src]'));
    const pageScript = scripts.find((script) => {
        const scriptUrl = new URL(script.getAttribute('src') ?? '', location.origin);
        const name = scriptUrl.pathname.split('/').pop() ?? '';
        return scriptUrl.origin === location.origin && scriptUrl.pathname.includes('/js/') &&
            name.endsWith('.js') && !sharedScripts.has(name);
    });
    return pageScript ? new URL(pageScript.getAttribute('src') ?? '', location.origin).href : null;
}

function scriptName(url: string): string {
    return new URL(url).pathname.split('/').pop() ?? '';
}

export function registerPage(name: string, initialize: PageInitializer): void {
    pages.set(name, initialize);
}

async function activatePage(): Promise<void> {
    const id = ++activationId;
    const controller = new AbortController();
    activeAbort = controller;
    configurePlayer({});
    const initialize = activeScript ? pages.get(scriptName(activeScript)) : null;
    if (!initialize) {
        return;
    }
    const cleanup = await initialize(controller.signal);
    if (id !== activationId) {
        cleanup?.();
        return;
    }
    activeCleanup = cleanup ?? null;
}

function deactivatePage(): void {
    activationId++;
    activeAbort?.abort();
    activeAbort = null;
    activeCleanup?.();
    activeCleanup = null;
}

function canNavigate(anchor: HTMLAnchorElement, event: MouseEvent): URL | null {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey ||
        event.shiftKey || event.altKey || anchor.hasAttribute('download') ||
        (anchor.target && anchor.target !== '_self') || anchor.dataset.noSoftNavigation !== undefined) {
        return null;
    }
    const url = new URL(anchor.href, location.href);
    if (url.origin !== location.origin || !['http:', 'https:'].includes(url.protocol) ||
        url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/') ||
        (url.pathname === location.pathname && url.search === location.search) ||
        /\.[^/]+$/.test(url.pathname)) {
        return null;
    }
    return url;
}

async function navigate(url: URL, replaceHistory: boolean, restoreY = 0): Promise<void> {
    request?.abort();
    const controller = new AbortController();
    request = controller;
    try {
        const response = await fetch(url.href, {
            signal: controller.signal,
            headers: { Accept: 'text/html' }
        });
        if (!response.ok || !response.headers.get('content-type')?.includes('text/html')) {
            throw new Error(`画面の取得に失敗しました: ${response.status}`);
        }
        const finalUrl = new URL(response.url);
        if (finalUrl.origin !== location.origin) {
            throw new Error('異なるオリジンへ転送されました。');
        }
        const page = new DOMParser().parseFromString(await response.text(), 'text/html');
        const nextMain = page.querySelector<HTMLElement>('main[role="main"]');
        const currentMain = document.querySelector<HTMLElement>('main[role="main"]');
        if (!nextMain || !currentMain || !page.getElementById('audio-player')) {
            throw new Error('画面の構造が一致しません。');
        }
        if (controller.signal.aborted) {
            return;
        }
        const nextScript = getPageScript(page);
        const previousScrollY = window.scrollY;
        deactivatePage();
        document.getElementById('notification-popup')?.classList.remove('active');
        document.getElementById('bottom-sheet')?.classList.remove('is-active');
        document.body.classList.remove('bottom-sheet-open');
        currentMain.replaceWith(document.importNode(nextMain, true));
        activeScript = nextScript;
        activeUrl = finalUrl;
        if (!replaceHistory) {
            history.replaceState({ scrollY: previousScrollY }, '', location.href);
            history.pushState({ scrollY: 0 }, '', finalUrl.href);
        }
        document.body.dataset.resumePlaybackAcrossPages = page.body.dataset.resumePlaybackAcrossPages ?? 'false';
        setPlayerPageTitle(page.title);
        if (nextScript && !pages.has(scriptName(nextScript))) {
            await import(nextScript);
        }
        if (nextScript && !pages.has(scriptName(nextScript))) {
            throw new Error(`画面の初期化処理が登録されていません: ${scriptName(nextScript)}`);
        }
        if (controller.signal.aborted) {
            return;
        }
        await activatePage();
        const main = document.querySelector<HTMLElement>('main[role="main"]');
        main?.setAttribute('tabindex', '-1');
        main?.focus({ preventScroll: true });
        if (finalUrl.hash) {
            document.getElementById(decodeURIComponent(finalUrl.hash.slice(1)))?.scrollIntoView();
        } else {
            window.scrollTo(0, restoreY);
        }
    } catch (error) {
        if (controller.signal.aborted) {
            return;
        }
        console.error('画面遷移に失敗しました。', error);
        if (replaceHistory) {
            location.reload();
        } else {
            location.assign(url.href);
        }
    } finally {
        if (request === controller) {
            request = null;
        }
    }
}

export function startPageNavigation(): void {
    if (started) {
        return;
    }
    started = true;
    if (document.body.dataset.resumePlaybackAcrossPages === 'true') {
        history.scrollRestoration = 'manual';
        history.replaceState({ scrollY: window.scrollY }, '', location.href);
    }
    void activatePage();
    document.addEventListener('click', (event) => {
        if (document.body.dataset.resumePlaybackAcrossPages !== 'true') {
            return;
        }
        const anchor = event.target instanceof Element
            ? event.target.closest<HTMLAnchorElement>('a[href]')
            : null;
        const url = anchor ? canNavigate(anchor, event) : null;
        if (!url) {
            return;
        }
        event.preventDefault();
        void navigate(url, false);
    });
    window.addEventListener('popstate', (event) => {
        if (location.pathname === activeUrl.pathname && location.search === activeUrl.search) {
            const target = location.hash ? document.getElementById(decodeURIComponent(location.hash.slice(1))) : null;
            if (target) {
                target.scrollIntoView();
            } else {
                window.scrollTo(0, Number(event.state?.scrollY) || 0);
            }
            return;
        }
        if (document.body.dataset.resumePlaybackAcrossPages !== 'true') {
            location.reload();
            return;
        }
        void navigate(new URL(location.href), true, Number(event.state?.scrollY) || 0);
    });
}
