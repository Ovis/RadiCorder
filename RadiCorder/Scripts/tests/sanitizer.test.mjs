import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { JSDOM } from 'jsdom';
import createDOMPurify from 'dompurify';

const source = readFileSync(new URL('../../wwwroot/js/utils.js', import.meta.url), 'utf8');
function sanitize(input) {
    const dom = new JSDOM('', { url: 'http://localhost/', runScripts: 'outside-only' });
    dom.window.DOMPurify = createDOMPurify(dom.window);
    dom.window.eval(source.replace(/^export /gm, '') + '\nwindow.reviewResult = sanitizeHtml;');
    const output = dom.window.reviewResult(input);
    const body = new dom.window.DOMParser().parseFromString(output, 'text/html').body;
    dom.window.close();
    return body;
}

for (const input of [
    '<div><p onclick="alert(1)">番組</p></div>',
    '<div><img src="invalid" onerror="alert(1)"></div>',
    '<div><a href="javascript:alert(1)">番組</a></div>',
    '<section><div><a href="java&#x09;script:alert(1)">番組</a></div></section>',
    '<svg><a onload="alert(1)">番組</a></svg>',
    '<math><mtext><img src="invalid" onerror="alert(1)"></mtext></math>'
]) {
    test(`危険な入れ子と属性を除去する: ${input}`, () => {
        const body = sanitize(input);
        assert.equal(body.querySelector('img,svg,math,script,iframe'), null);
        for (const el of body.querySelectorAll('*')) {
            for (const attribute of el.attributes) assert.ok(!attribute.name.startsWith('on'));
            assert.ok(!/javascript:/i.test(el.getAttribute('href') ?? ''));
        }
    });
}

test('番組説明の書式と安全なリンクを維持する', () => {
    const body = sanitize('<div><p>番組<br><strong>出演者</strong> <a href="https://example.test/program" target="_blank" title="詳細">詳細</a></p></div>');
    assert.equal(body.querySelector('p').textContent, '番組出演者 詳細');
    assert.ok(body.querySelector('br'));
    assert.ok(body.querySelector('strong'));
    assert.equal(body.querySelector('a').getAttribute('href'), 'https://example.test/program');
    assert.equal(body.querySelector('a').getAttribute('rel'), 'noopener noreferrer');
});

test('相対URLとメールを維持しその他のプロトコルを拒否する', () => {
    const body = sanitize('<a href="/program">相対</a><a href="mailto:radio@example.test">メール</a><a href="ftp://example.test">FTP</a>');
    const links = body.querySelectorAll('a');
    assert.equal(links[0].getAttribute('href'), '/program');
    assert.equal(links[1].getAttribute('href'), 'mailto:radio@example.test');
    assert.equal(links[2].getAttribute('href'), null);
});
