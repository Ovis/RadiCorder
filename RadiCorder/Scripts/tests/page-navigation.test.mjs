import test from 'node:test';
import assert from 'node:assert/strict';
import { JSDOM } from 'jsdom';

test('画面遷移と戻る操作で同じプレーヤーを維持し、ページを再初期化する', async () => {
    const origin = 'https://radi.example';
    const homeHtml = `<!doctype html><html><head><title>ホーム - RadiCorder</title></head>
        <body data-resume-playback-across-pages="true">
        <main role="main"><a id="next" href="/Program/Search">検索</a></main>
        <footer><div id="audio-player"><div class="player-container"><audio id="audio-player-elm"></audio></div></div></footer>
        <script type="module" src="/js/layout.js"></script><script type="module" src="/js/home.js"></script>
        </body></html>`;
    const searchHtml = `<!doctype html><html><head><title>検索 - RadiCorder</title></head>
        <body data-resume-playback-across-pages="true">
        <main role="main"><a id="previous" href="/">ホーム</a><a id="plain" href="/Program">番組</a><p id="search-page">検索画面</p></main>
        <footer><div id="audio-player"></div></footer>
        <script type="module" src="/js/layout.js"></script><script type="module" src="/js/search.js"></script>
        </body></html>`;
    const plainHtml = `<!doctype html><html><head><title>番組 - RadiCorder</title></head>
        <body data-resume-playback-across-pages="true">
        <main role="main"><h1 id="plain-page">番組</h1></main>
        <footer><div id="audio-player"></div></footer>
        <script type="module" src="/js/layout.js"></script></body></html>`;
    const dom = new JSDOM(homeHtml, { url: `${origin}/` });
    const { window } = dom;
    window.scrollTo = () => {};
    Object.assign(globalThis, {
        window,
        document: window.document,
        location: window.location,
        history: window.history,
        DOMParser: window.DOMParser,
        Element: window.Element,
        AbortController: window.AbortController,
        localStorage: window.localStorage,
        fetch: async (url) => ({
            ok: true,
            url,
            headers: { get: () => 'text/html; charset=utf-8' },
            text: async () => {
                const pathname = new URL(url).pathname;
                return pathname === '/' ? homeHtml : pathname === '/Program' ? plainHtml : searchHtml;
            }
        })
    });

    const { registerPage, startPageNavigation } = await import('../../wwwroot/js/page-navigation.js');
    let homeActivations = 0;
    let searchActivations = 0;
    let homeCleanups = 0;
    registerPage('home.js', () => {
        homeActivations++;
        return () => { homeCleanups++; };
    });
    registerPage('search.js', () => {
        searchActivations++;
    });
    const audio = document.getElementById('audio-player-elm');
    startPageNavigation();
    await new Promise(resolve => setTimeout(resolve, 0));
    document.getElementById('next').click();
    await new Promise(resolve => setTimeout(resolve, 20));

    assert.equal(location.pathname, '/Program/Search');
    assert.ok(document.getElementById('search-page'));
    assert.strictEqual(document.getElementById('audio-player-elm'), audio);
    assert.equal(homeCleanups, 1);
    assert.equal(searchActivations, 1);

    history.back();
    await new Promise(resolve => setTimeout(resolve, 40));
    assert.equal(location.pathname, '/');
    assert.ok(document.getElementById('next'));
    assert.strictEqual(document.getElementById('audio-player-elm'), audio);
    assert.equal(homeActivations, 2);

    history.forward();
    await new Promise(resolve => setTimeout(resolve, 40));
    assert.equal(location.pathname, '/Program/Search');
    assert.ok(document.getElementById('search-page'));
    assert.strictEqual(document.getElementById('audio-player-elm'), audio);
    assert.equal(searchActivations, 2);

    document.getElementById('plain').click();
    await new Promise(resolve => setTimeout(resolve, 20));
    assert.equal(location.pathname, '/Program');
    assert.ok(document.getElementById('plain-page'));
    assert.strictEqual(document.getElementById('audio-player-elm'), audio);
    dom.window.close();
});
