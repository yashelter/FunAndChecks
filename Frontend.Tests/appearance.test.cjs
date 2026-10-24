const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../Frontend/wwwroot/js/dashboard-appearance.js'), 'utf8');

function appearance({ stored = {}, blocked = false } = {}) {
    const storage = new Map(Object.entries(stored));
    const window = {};
    const document = {
        documentElement: { dataset: {}, style: { setProperty() { } } },
        cookie: ''
    };
    const localStorage = {
        getItem: key => { if (blocked) throw Error('Blocked'); return storage.get(key) ?? null; },
        setItem: (key, value) => { if (blocked) throw Error('Blocked'); storage.set(key, value); }
    };
    vm.runInNewContext(source, { window, document, localStorage, location: { protocol: 'https:' } });
    return { app: window.dashboardAppearance, storage, document };
}

test('seasonal themes are on by default', () => {
    // Ничего не выключено — сезонная тема показывается в своё окно без ручного переключения.
    assert.deepEqual([...appearance().app.load().disabledSeasons], []);
});

test('a theme the user turned off stays off after a reload', () => {
    const { app } = appearance({ stored: { 'fc-seasons-off': '["halloween"]' } });
    assert.deepEqual([...app.load().disabledSeasons], ['halloween']);
});

test('the old halloween switch is carried over', () => {
    assert.deepEqual([...appearance({ stored: { 'fc-halloween': 'off' } }).app.load().disabledSeasons], ['halloween']);
    assert.deepEqual([...appearance({ stored: { 'fc-halloween': 'on' } }).app.load().disabledSeasons], []);
});

test('the new list wins over the old halloween switch', () => {
    const { app } = appearance({ stored: { 'fc-halloween': 'off', 'fc-seasons-off': '[]' } });
    assert.deepEqual([...app.load().disabledSeasons], []);
});

test('broken storage values are ignored', () => {
    assert.deepEqual([...appearance({ stored: { 'fc-seasons-off': 'not json' } }).app.load().disabledSeasons], []);
    assert.deepEqual([...appearance({ stored: { 'fc-seasons-off': '[1, "halloween", null]' } }).app.load().disabledSeasons], ['halloween']);
});

test('saving the appearance stores the turned-off themes', () => {
    const { app, storage } = appearance();

    app.apply(false, false, null, '#66e4c1', '#09251f', ['halloween'], true);

    assert.equal(storage.get('fc-seasons-off'), '["halloween"]');
});

test('turning every theme back on is stored as well', () => {
    const { app, storage } = appearance({ stored: { 'fc-seasons-off': '["halloween"]' } });

    app.apply(false, false, null, '#66e4c1', '#09251f', [], true);

    assert.equal(storage.get('fc-seasons-off'), '[]');
});

test('blocked storage does not break the appearance', () => {
    const { app } = appearance({ blocked: true });

    // При недоступном хранилище применяются дефолты — все сезонные темы включены.
    assert.deepEqual([...app.load().disabledSeasons], []);
    app.apply(false, false, null, '#66e4c1', '#09251f', [], true);
});
