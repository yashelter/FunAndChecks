const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../Frontend/wwwroot/js/dashboard-bootstrap.js'), 'utf8');

function bootstrap({ languages = ['en-US'], stored = null, blocked = false, legacy = false, captions = {}, fetchFails = false } = {}) {
    const storage = new Map(stored ? [['culture_preference', stored]] : []);
    const window = {};
    const document = { documentElement: { dataset: { uiVersion: legacy ? 'legacy' : 'dashboard' } } };
    const localStorage = {
        getItem: key => { if (blocked) throw Error('Blocked'); return storage.get(key) ?? null; },
        setItem: (key, value) => { if (blocked) throw Error('Blocked'); storage.set(key, value); }
    };
    vm.runInNewContext(source, { window, document, localStorage, navigator: { languages },
        fetch: async () => { if (fetchFails) throw Error('Offline'); return { ok: true, json: async () => captions }; } });
    return { ...window, storage, document };
}

test('first visit follows preferred supported device language, not any secondary Russian entry', () => {
    const app = bootstrap({ languages: ['en-GB', 'ru-RU'] });
    assert.equal(app.culturePreference.get(), 'en-US');
    assert.equal(app.storage.get('culture_preference'), 'en-US');
    assert.equal(bootstrap({ languages: ['ru', 'en-US'] }).culturePreference.get(), 'ru-RU');
    assert.equal(bootstrap({ languages: ['de-DE', 'ru-RU', 'en-US'] }).culturePreference.get(), 'ru-RU');
});
test('saved choice wins over device and invalid saved values fall back safely', () => {
    assert.equal(bootstrap({ languages: ['ru-RU'], stored: 'en-US' }).culturePreference.get(), 'en-US');
    assert.equal(bootstrap({ languages: ['ru-RU'], stored: 'bogus' }).culturePreference.get(), 'ru-RU');
    assert.equal(bootstrap({ languages: [] }).culturePreference.get(), 'en-US');
});
test('blocked storage still allows device culture and manual change without reload loop', () => {
    const app = bootstrap({ languages: ['ru-RU'], blocked: true });
    assert.equal(app.culturePreference.get(), 'ru-RU');
    assert.equal(app.culturePreference.set('en-US'), false);
    assert.equal(app.document.documentElement.lang, 'en-US');
    assert.equal(app.culturePreference.set('invalid'), false);
});
test('legacy retains previous secondary-language detection without auto-saving', () => {
    const app = bootstrap({ languages: ['en-US', 'ru-RU'], legacy: true });
    assert.equal(app.culturePreference.get(), 'ru-RU');
    assert.equal(app.storage.size, 0);
});
test('captions accept only nonempty resource strings, stable within the page', async () => {
    const app = bootstrap({ captions: { 'ru-RU': [null, 42, '', '  строка  '] } });
    assert.equal(await app.dashboardCaptions.get('ru-RU'), 'строка');
    assert.equal(await app.dashboardCaptions.get('ru-RU'), 'строка');
    assert.equal(await app.dashboardCaptions.get('en-US'), '');
});
test('missing caption resource does not prevent application startup', async () => {
    assert.equal(await bootstrap({ fetchFails: true }).dashboardCaptions.get('ru-RU'), '');
});
