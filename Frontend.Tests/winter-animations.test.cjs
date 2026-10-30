const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..');
const css = fs.readFileSync(path.join(root, 'Frontend.Shared/wwwroot/dashboard-content.css'), 'utf8');
const razor = fs.readFileSync(path.join(root, 'Frontend.Shared/UI/WinterAnimations.razor'), 'utf8');
const theme = fs.readFileSync(path.join(root, 'Frontend.Shared/Seasons/WinterTheme.cs'), 'utf8');
const brandMark = fs.readFileSync(path.join(root, 'Frontend.Shared/UI/WinterBrandMark.razor'), 'utf8');
const index = fs.readFileSync(path.join(root, 'Frontend/wwwroot/index.html'), 'utf8');

/** Убирает комментарии, чтобы не считать фигурные скобки внутри них. */
function stripComments(source) { return source.replace(/\/\*[\s\S]*?\*\//g, ''); }

function braceBalance(source) {
    let depth = 0;
    for (const ch of stripComments(source)) {
        if (ch === '{') depth++;
        else if (ch === '}') depth--;
    }
    return depth;
}

/** Правила верхнего уровня (селектор → тело), вложенные в @media — с префиксом условия. */
function topLevelRules(source) {
    const clean = stripComments(source);
    const rules = [];
    const stack = [];
    let buffer = '';
    for (const ch of clean) {
        if (ch === '{') { stack.push(buffer.trim()); buffer = ''; }
        else if (ch === '}') {
            const selector = stack.pop();
            if (buffer.trim()) rules.push({ selector, context: [...stack], body: buffer.trim() });
            buffer = '';
        }
        else buffer += ch;
    }
    return rules;
}

const rules = topLevelRules(css);
const inMedia = pattern => rules.filter(r => r.context.some(c => pattern.test(c)));
const plain = selector => rules.filter(r => r.context.length === 0 && r.selector.split(',').map(s => s.trim()).includes(selector));

test('winter animation section is not nested inside an unclosed block', () => {
    const marker = css.indexOf('Winter Animations');
    assert.ok(marker > 0, 'Winter animation section is missing from dashboard-content.css');
    assert.equal(braceBalance(css.slice(0, marker)), 0, 'Winter section is nested inside an unclosed block');
});

test('winter keyframes are defined', () => {
    for (const name of ['fc-wt-fall', 'fc-wt-sway', 'fc-wt-gust', 'fc-wt-veil', 'fc-wt-bulb-pulse', 'fc-wt-bulb-rainbow',
        'fc-wt-snowman-sway', 'fc-wt-snowman-shake', 'fc-wt-scarf', 'fc-wt-logo-spin']) {
        assert.ok(css.includes(`@keyframes ${name}`), `missing @keyframes ${name}`);
    }
});

test('the garland has a class for every mode the component switches to', () => {
    const modes = razor.match(/GarlandModes\s*=\s*\[([^\]]+)\]/)[1].match(/"(\w+)"/g).map(m => m.slice(1, -1));
    assert.deepEqual(modes, ['chase', 'twinkle', 'rainbow', 'steady']);
    for (const mode of modes.filter(m => m !== 'steady'))
        assert.ok(rules.some(r => r.selector.includes(`.fc-wt-garland-${mode} .fc-wt-bulb`) && /animation:/.test(r.body)), `mode ${mode} has no animation`);
    assert.match(razor, /fc-wt-garland-@GarlandMode/);
});

test('only the garland bulbs and the snowman take clicks', () => {
    assert.match(plain('.fc-winter-animations')[0].body, /pointer-events:\s*none/);
    assert.match(plain('.fc-wt-garland')[0].body, /pointer-events:\s*none/, 'the garland itself must not cover the top bar buttons');
    assert.match(plain('.fc-wt-bulb')[0].body, /pointer-events:\s*auto/);
    assert.match(plain('.fc-wt-snowman')[0].body, /pointer-events:\s*auto/);
    assert.match(razor, /<button type="button" class="fc-wt-garland[^>]*aria-label=[^>]*@onclick="NextGarlandMode"/);
    assert.match(razor, /<button type="button" class="fc-wt-snowman"[^>]*aria-label=[^>]*@onclick="StartBlizzard"/);
});

test('bulbs hang on the wire: the wire sags by the formula the component uses', () => {
    assert.match(razor, /M0 2 Q125 22 250 2 Q375 22 500 2 Q625 22 750 2 Q875 22 1000 2/);
    assert.match(razor, /2 \+ 40 \* t \* \(1 - t\)/, 'quadratic curve with control y=22 peaks at y = 2 + 40·t·(1−t)');
});

test('phones get the decorations too, smaller and without catching taps on the garland', () => {
    const phone = inMedia(/max-width:\s*760px/);
    assert.ok(phone.some(r => /\.fc-wt-bulb\b/.test(r.selector) && /pointer-events:\s*none/.test(r.body)), 'bulbs must not catch taps on phones');
    for (const part of ['.fc-wt-frost', '.fc-wt-drift', '.fc-wt-snowman'])
        assert.ok(phone.some(r => r.selector.includes(part) && /width:/.test(r.body)), `${part} is resized for phones`);
    const hiddenOnSmall = [...phone, ...inMedia(/max-width:\s*600px/)].filter(r => /display:\s*none/.test(r.body)).map(r => r.selector).join(',');
    assert.ok(!/fc-wt-frost|fc-wt-drift|fc-wt-snowman\b/.test(hiddenOnSmall), 'frost, drifts and the snowman stay on phones');
    assert.match(hiddenOnSmall, /\.fc-wt-snow \.fc-wt-flake:nth-child\(n\+15\)/, 'small screens show fewer snowflakes');
});

test('only the moving parts are hidden for reduced motion; frost, drifts and the snowman stay', () => {
    const reduced = inMedia(/prefers-reduced-motion/).filter(r => /fc-wt|fc-winter/.test(r.selector));
    const hidden = reduced.filter(r => /display:\s*none/.test(r.body)).map(r => r.selector).join(',');
    assert.match(hidden, /\.fc-wt-snow\b/);
    assert.match(hidden, /\.fc-wt-blizzard/);
    assert.ok(!/fc-wt-frost|fc-wt-drift|fc-wt-snowman|fc-wt-garland/.test(hidden), 'static decorations must stay');
    const stopped = reduced.filter(r => /animation:\s*none/.test(r.body)).map(r => r.selector).join(',');
    for (const part of ['.fc-wt-bulb', '.fc-wt-snowman svg', '.fc-wt-scarf-tail', '.fc-winter-flake'])
        assert.ok(stopped.includes(part), `${part} must stop moving`);
});

test('inline numbers in styles do not depend on the UI culture', () => {
    // В русской культуре 0.8 печатается как «0,8» — такой CSS браузер отбросит.
    assert.match(razor, /CultureInfo\.InvariantCulture/);
    assert.match(razor, /--o:\{Css\(f\.Opacity\)\}/);
    assert.match(razor, /--x:@Css\(bulb\.X\)%/);
});

test('winter component disposes its timer', () => {
    assert.match(razor, /@implements\s+IDisposable/);
    assert.match(razor, /_blizzardTimer\?\.Dispose\(\)/);
});

test('winter theme plugs in its animations and the snowflake logo', () => {
    assert.match(theme, /Decorations\s*=>\s*typeof\(WinterAnimations\)/);
    assert.match(theme, /BrandMark\s*=>\s*typeof\(WinterBrandMark\)/);
    assert.match(theme, /new\(12,\s*1,\s*1,\s*30\)/, 'winter runs from 1 December to 30 January');
    assert.ok(brandMark.includes('fc-winter-logo') && brandMark.includes('fc-winter-flake'));
});

test('dashboard-content.css cache version includes the winter styles', () => {
    const match = index.match(/dashboard-content\.css\?v=(\d+)/);
    assert.ok(match, 'dashboard-content.css must be referenced with a cache-busting version');
    assert.ok(Number(match[1]) >= 13, `cache version must be >= 13, got ${match[1]}`);
});
