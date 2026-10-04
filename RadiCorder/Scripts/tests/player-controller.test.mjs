import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../../wwwroot/js/player-controller.js', import.meta.url), 'utf8')
    .replace(/import[\s\S]*?from '[^']+';\r?\n/g, '')
    .replace(/^export /gm, '');

function createHarness(initialState = null) {
    const storage = new Map(initialState ? [['radicorder-player-state', JSON.stringify(initialState)]] : []);
    const elements = new Map();
    const messages = [];
    const players = [];

    class Element {
        constructor(tagName) {
            this.tagName = tagName;
            this.children = [];
            this.listeners = new Map();
            this.style = {};
            this.attributes = new Map();
            this.paused = true;
            this.ended = false;
            this.currentTime = 0;
            this.playbackRate = 1;
            this.playCalls = 0;
        }
        set id(value) { this._id = value; elements.set(value, this); }
        get id() { return this._id; }
        set innerHTML(_) { this.replaceChildren(); }
        append(...children) { for (const child of children) { child.parent = this; this.children.push(child); } }
        appendChild(child) { this.append(child); }
        replaceChildren() { for (const child of this.children) { if (child.id) elements.delete(child.id); } this.children = []; }
        remove() { this.parent.children = this.parent.children.filter(child => child !== this); }
        addEventListener(name, handler) { this.listeners.set(name, handler); }
        dispatchEvent(event) { this.listeners.get(event.type)?.(event); }
        setAttribute(name, value) { this.attributes.set(name, value); }
        removeAttribute(name) { this.attributes.delete(name); }
        canPlayType() { return ''; }
        load() {}
        pause() { this.paused = true; this.dispatchEvent({ type: 'pause' }); }
        play() { this.playCalls++; this.paused = false; this.dispatchEvent({ type: 'play' }); return Promise.resolve(); }
        closest() { return this.parent?.parent ?? null; }
        querySelector(selector) { return this.children.find(child => child.className === selector.slice(1)) ?? null; }
    }

    class Hls {
        static Events = { MANIFEST_PARSED: 'manifest', ERROR: 'error' };
        static isSupported() { return true; }
        constructor() { this.config = {}; this.handlers = new Map(); this.destroyed = false; players.push(this); }
        on(name, callback) { this.handlers.set(name, callback); }
        loadSource(url) { this.url = url; }
        attachMedia(audio) { this.audio = audio; }
        destroy() { this.destroyed = true; }
        emit(name, data) { this.handlers.get(name)?.(name, data); }
    }

    const footer = new Element('footer');
    footer.id = 'audio-player';
    const context = {
        document: {
            title: '録音一覧 - RadiCorder',
            referrer: 'https://radio.test/Recorded',
            body: { dataset: { resumePlaybackAcrossPages: 'true' } },
            getElementById: id => elements.get(id) ?? null,
            createElement: tag => new Element(tag)
        },
        window: { location: { origin: 'https://radio.test' }, Hls, addEventListener() {} },
        DOMException,
        URL,
        Event,
        createStandardPlayerJumpControls: () => { const controls = new Element('div'); controls.className = 'player-jump-controls'; return controls; },
        disposePlayerJumpControls() {},
        applyPlaybackRate: (audio, rate) => { audio.playbackRate = rate; },
        playerPlaybackRateOptions: [1, 1.25, 1.5, 1.75, 2],
        clearPersistedPlayerState: () => storage.delete('radicorder-player-state'),
        readPersistedPlayerState: () => JSON.parse(storage.get('radicorder-player-state') ?? 'null'),
        writePersistedPlayerState: state => storage.set('radicorder-player-state', JSON.stringify(state)),
        showGlobalToast: message => messages.push(message)
    };
    runInNewContext(source + '\nglobalThis.api = { restorePlayer, playPlayerSource, stopPlayer, getPlayerAudio };', context);
    return { api: context.api, context, storage, players, messages };
}

test('録音の一時停止状態をページ遷移後も維持する', async () => {
    const state = {
        sourceUrl: '/api/recordings/play/123', recordId: '123', currentTime: 42,
        playbackRate: 1.5, wasPlaying: false, savedAtUtc: new Date().toISOString()
    };
    const { api, players, storage } = createHarness(state);
    await api.restorePlayer();
    players[0].emit('manifest');
    assert.equal(api.getPlayerAudio().playCalls, 0);
    assert.equal(api.getPlayerAudio().currentTime, 42);
    assert.equal(JSON.parse(storage.get('radicorder-player-state')).wasPlaying, false);
});

test('次の番組へ切り替える際に古い HLS 接続を閉じる', async () => {
    const { api, players } = createHarness();
    await api.playPlayerSource({ sourceUrl: '/first.m3u8' });
    await api.playPlayerSource({ sourceUrl: '/second.m3u8' });
    assert.equal(players[0].destroyed, true);
    assert.equal(players[1].url, '/second.m3u8');
    players[0].emit('manifest');
    assert.equal(api.getPlayerAudio().playCalls, 0);
    players[1].emit('manifest');
    assert.equal(api.getPlayerAudio().playCalls, 1);
});

test('自動再生拒否を通知し、手動再生できるプレーヤーを残す', async () => {
    const { api, players, messages, storage } = createHarness();
    await api.playPlayerSource({ sourceUrl: '/program.m3u8' });
    const audio = api.getPlayerAudio();
    audio.play = () => Promise.reject(new DOMException('blocked', 'NotAllowedError'));
    players[0].emit('manifest');
    await new Promise(resolve => setImmediate(resolve));
    assert.match(messages[0], /自動再生/);
    assert.ok(api.getPlayerAudio());
    assert.equal(JSON.parse(storage.get('radicorder-player-state')).wasPlaying, false);
});

test('ページ間の再生復帰を無効にした場合は保存状態を破棄する', async () => {
    const state = { sourceUrl: '/program.m3u8', savedAtUtc: new Date().toISOString() };
    const { api, context, storage, players } = createHarness(state);
    context.document.body.dataset.resumePlaybackAcrossPages = 'false';
    await api.restorePlayer();
    assert.equal(storage.size, 0);
    assert.equal(players.length, 0);
});

test('似た文字列の別オリジンからは再生を復帰しない', async () => {
    const state = { sourceUrl: '/program.m3u8', savedAtUtc: new Date().toISOString() };
    const { api, context, storage, players } = createHarness(state);
    context.document.referrer = 'https://radio.test.evil.example/';
    await api.restorePlayer();
    assert.equal(storage.size, 0);
    assert.equal(players.length, 0);
});
