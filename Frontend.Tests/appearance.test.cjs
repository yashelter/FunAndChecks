const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../Frontend/wwwroot/js/dashboard-appearance.js'), 'utf8');

function appearance({ stored = null, blocked = false } = {}) {
    const storage = new Map(stored === null ? [] : [['fc-halloween', stored]]);
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

test('halloween mode is on by default', () => {
    // Дефолт включён: событие показывается сразу, без ручного переключения.
    const { app } = appearance();
    assert.equal(app.load().isHalloweenMode, true);
});

test('halloween mode stays off once the user turns it off', () => {
    assert.equal(appearance({ stored: 'off' }).app.load().isHalloweenMode, false);
    assert.equal(appearance({ stored: 'on' }).app.load().isHalloweenMode, true);
});

test('halloween mode survives a reload', () => {
    assert.equal(appearance({ stored: 'on' }).app.load().isHalloweenMode, true);
});

test('saving the appearance stores the halloween mode in local storage', () => {
    const { app, storage } = appearance();

    app.apply(false, false, null, '#66e4c1', '#09251f', true, true);

    assert.equal(storage.get('fc-halloween'), 'on');
});

test('turning the halloween mode off is stored as well', () => {
    const { app, storage } = appearance({ stored: 'on' });

    app.apply(false, false, null, '#66e4c1', '#09251f', false, true);

    assert.equal(storage.get('fc-halloween'), 'off');
});

test('blocked storage does not break the appearance', () => {
    const { app } = appearance({ blocked: true });

    // При недоступном хранилище применяются дефолты — как и для темы (тёмная).
    assert.equal(app.load().isHalloweenMode, true);
    app.apply(false, false, null, '#66e4c1', '#09251f', true, true);
});
