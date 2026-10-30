const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.join(__dirname, '..');
const css = fs.readFileSync(path.join(root, 'Frontend.Shared/wwwroot/dashboard-content.css'), 'utf8');
const razor = fs.readFileSync(path.join(root, 'Frontend.Shared/UI/HalloweenAnimations.razor'), 'utf8');
const shell = fs.readFileSync(path.join(root, 'Frontend.Shared/UI/WorkspaceShell.razor'), 'utf8');
const index = fs.readFileSync(path.join(root, 'Frontend/wwwroot/index.html'), 'utf8');
const authLayout = fs.readFileSync(path.join(root, 'Frontend.Shared/Layout/AuthLayout.razor'), 'utf8');
const program = fs.readFileSync(path.join(root, 'Frontend/Program.cs'), 'utf8');
const theme = fs.readFileSync(path.join(root, 'Frontend.Shared/Seasons/HalloweenTheme.cs'), 'utf8');
const brandMark = fs.readFileSync(path.join(root, 'Frontend.Shared/UI/HalloweenBrandMark.razor'), 'utf8');

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

/** Возвращает полный текст первого блока, начинающегося с заголовка (@keyframes name, селектор и т. п.). */
function blockBody(header, source = css) {
    const start = source.indexOf(header);
    assert.ok(start >= 0, `missing ${header}`);
    const open = source.indexOf('{', start);
    let depth = 0;
    for (let i = open; i < source.length; i++) {
        if (source[i] === '{') depth++;
        else if (source[i] === '}') {
            depth--;
            if (depth === 0) return source.slice(open, i + 1);
        }
    }
    return source.slice(open);
}
const keyframesBody = name => blockBody(`@keyframes ${name}`);

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

test('dashboard-content.css has balanced braces', () => {
    assert.equal(braceBalance(css), 0, 'CSS has unbalanced braces — rules after the break are ignored by the browser');
});

test('halloween animation section is not nested inside an unclosed block', () => {
    const marker = css.indexOf('Halloween Animations');
    assert.ok(marker > 0, 'Halloween animation section is missing from dashboard-content.css');
    assert.equal(braceBalance(css.slice(0, marker)), 0, 'Halloween section is nested inside an unclosed block');
});

test('only the moving parts are hidden for reduced motion; webs and the lantern stay', () => {
    const reduced = topLevelRules(css).filter(r => r.context.some(c => /prefers-reduced-motion/.test(c)) && /fc-hw|fc-halloween/.test(r.selector));
    const hidden = reduced.filter(r => /display:\s*none/.test(r.body)).map(r => r.selector).join(',');
    for (const moving of ['.fc-halloween-ghost', '.fc-hw-spider-drop', '.fc-hw-bats']) {
        assert.ok(hidden.includes(moving), `${moving} must be hidden for reduced motion`);
    }
    for (const still of ['.fc-halloween-animations', '.fc-hw-web', '.fc-hw-lantern', '.fc-hw-fog']) {
        assert.ok(!hidden.split(',').map(s => s.trim()).includes(still), `${still} must stay visible for reduced motion`);
    }
    const stopped = reduced.filter(r => /animation:\s*none/.test(r.body)).map(r => r.selector).join(',');
    assert.ok(stopped.includes('.fc-hw-lantern') && stopped.includes('.fc-hw-fog'), 'lantern and fog must stop moving');
});

test('halloween keyframes are defined', () => {
    for (const name of ['ghost-path-1', 'ghost-path-2', 'ghost-path-3', 'ghost-path-4', 'ghost-path-5',
        'fc-hw-spider-drop', 'fc-hw-spider-sway', 'fc-hw-spider-flee', 'fc-hw-legs',
        'fc-hw-bat-fly', 'fc-hw-bat-burst', 'fc-hw-wings', 'fc-hw-fog-drift', 'fc-hw-lantern-glow', 'fc-hw-lantern-flicker']) {
        assert.ok(css.includes(`@keyframes ${name}`), `missing @keyframes ${name}`);
    }
});

test('there are five ghosts on staggered infinite paths across the whole screen', () => {
    const durations = new Set();
    for (let i = 1; i <= 5; i++) {
        assert.ok(razor.includes(`fc-halloween-ghost-${i}`), `ghost ${i} is not rendered`);
        const rule = css.match(new RegExp(`\\.fc-halloween-ghost-${i}\\s*\\{[^}]*animation:\\s*ghost-path-${i}\\s+([\\d.]+)s[^}]*infinite`));
        assert.ok(rule, `ghost ${i} must loop forever on its own path`);
        durations.add(rule[1]);
        const body = keyframesBody(`ghost-path-${i}`);
        assert.match(body, /translate3d\(-100px|translate3d\(calc\(100vw/, `ghost ${i} must start off-screen`);
        assert.match(body, /100vw/, `ghost ${i} must travel across the full width`);
    }
    assert.ok(durations.size >= 4, `ghost durations must differ, got ${[...durations].join(', ')}`);
    assert.match(blockBody('.fc-halloween-ghost {'), /opacity:\s*0/, 'ghosts must be invisible outside the animation');
});

test('spider hangs on a separate thread: the thread grows, the spider never spins', () => {
    // Раньше нить была псевдоэлементом паука и крутилась вместе с ним на 540°.
    assert.match(razor, /class="fc-hw-thread"[\s\S]*class="fc-hw-spider"/, 'the spider must hang inside its own thread element');
    assert.match(blockBody('.fc-hw-thread {'), /animation:\s*fc-hw-spider-drop/, 'the thread length must be animated');
    assert.match(keyframesBody('fc-hw-spider-drop'), /height:\s*calc\(30vh/, 'the thread must reach into the page');
    for (const name of ['fc-hw-spider-drop', 'fc-hw-spider-sway', 'fc-hw-legs']) {
        for (const [, deg] of keyframesBody(name).matchAll(/rotate\((-?[\d.]+)deg\)/g)) {
            assert.ok(Math.abs(Number(deg)) <= 10, `${name} rotates by ${deg}deg — the spider must only sway`);
        }
    }
    assert.doesNotMatch(css, /\.fc-hw-spider[^{]*::before/, 'the thread must not be a pseudo-element of the spider');
});

test('spider descent fits into the restart interval', () => {
    const cycle = Number(blockBody('.fc-hw-thread {').match(/fc-hw-spider-drop\s+([\d.]+)s/)[1]);
    const interval = Number(razor.match(/SpiderIntervalMs\s*=\s*(\d+)/)[1]) / 1000;
    const frames = keyframesBody('fc-hw-spider-drop');
    const visible = [...frames.matchAll(/([\d.]+)%\s*\{\s*height:\s*calc/g)].map(m => Number(m[1]));
    const lastVisible = Math.max(...visible);
    assert.ok((lastVisible / 100) * cycle < interval, 'the spider must finish its visit before the timer restarts it');
    assert.ok(cycle < interval, `the descent (${cycle}s) must be shorter than the restart interval (${interval}s)`);
});

test('only the spider and the lantern take clicks', () => {
    assert.match(blockBody('.fc-halloween-animations {'), /pointer-events:\s*none/);
    assert.match(blockBody('.fc-hw-spider {'), /pointer-events:\s*auto/);
    assert.match(blockBody('.fc-hw-lantern {'), /pointer-events:\s*auto/);
    assert.match(razor, /<button type="button" class="fc-hw-spider" aria-label="@Loc\["Halloween_SpiderLabel"\]"[^>]*@onclick="FleeSpider"/);
    assert.match(razor, /<button type="button" class="fc-hw-lantern" aria-label="@Loc\["Halloween_LanternLabel"\]"[^>]*@onclick="ReleaseBats"/);
    // Туман поверх контента, поэтому он прозрачный и гаснет кверху — текст у нижнего края читается.
    assert.match(blockBody('.fc-hw-fog {'), /pointer-events:\s*none[\s\S]*mask-image:\s*linear-gradient\(to top/, 'fog must not block clicks and must fade upwards');
});

test('bats flap their wings and either cross the screen or burst out of the lantern', () => {
    assert.match(blockBody('.fc-hw-wing {'), /animation:\s*fc-hw-wings/);
    const fold = Math.min(...[...keyframesBody('fc-hw-wings').matchAll(/scaleY\(([-\d.]+)\)/g)].map(m => Number(m[1])));
    assert.ok(fold > 0.2 && fold < 0.6, `wings must fold noticeably but never flatten into a line, got scaleY(${fold})`);
    assert.match(keyframesBody('fc-hw-bat-fly'), /100vw/, 'a flock must cross the whole width');
    assert.match(keyframesBody('fc-hw-bat-burst'), /calc\(100vw - \d+px\),\s*calc\(100vh - \d+px\)/, 'a burst must start at the lantern');
    assert.ok((razor.match(/\(\d+, \d+, [\d.]+, [\d.]+\)/g) || []).length >= 5, 'a flock needs several bats');
});

test('phones get the decorations too, just smaller', () => {
    const phone = topLevelRules(css).filter(r => r.context.some(c => /max-width:\s*760px/.test(c))).map(r => r.selector).join(',');
    for (const part of ['.fc-hw-web', '.fc-hw-lantern', '.fc-hw-spider', '.fc-halloween-ghost', '.fc-hw-fog']) {
        assert.ok(phone.includes(part), `${part} needs a phone size`);
    }
    const narrow = topLevelRules(css).filter(r => r.context.some(c => /max-width:\s*600px/.test(c)));
    assert.ok(narrow.some(r => /fc-halloween-ghost-4/.test(r.selector) && /display:\s*none/.test(r.body)), 'small screens show fewer ghosts');
    assert.ok(!narrow.some(r => /fc-hw-web|fc-hw-lantern/.test(r.selector) && /display:\s*none/.test(r.body)), 'webs and the lantern stay on phones');
    // Паук на телефоне висит над кнопками и не должен перехватывать нажатия.
    assert.ok(topLevelRules(css).some(r => r.context.some(c => /max-width:\s*760px/.test(c)) && /\.fc-hw-spider\b/.test(r.selector) && /pointer-events:\s*none/.test(r.body)), 'the spider must not catch taps on phones');
});

test('inline numbers in styles do not depend on the UI culture', () => {
    // В русской культуре 0.8 печатается как «0,8» — такой CSS браузер отбросит.
    assert.match(razor, /--bat-scale:@Css\(bat\.Scale\)/);
    assert.match(razor, /CultureInfo\.InvariantCulture/);
});

test('seasonal decorations and brand mark are mounted in both layouts', () => {
    for (const [name, source] of [['WorkspaceShell', shell], ['AuthLayout', authLayout]]) {
        assert.ok(source.includes('<SeasonalDecorations />'), `${name} must render <SeasonalDecorations />`);
        assert.ok(source.includes('<BrandMark />'), `${name} must render <BrandMark />`);
        assert.ok(!/HalloweenAnimations|IsHalloweenMode|Flags\./.test(source), `${name} must not know about a specific season`);
    }
});

test('halloween theme plugs in its animations and the pumpkin logo', () => {
    assert.match(theme, /Decorations\s*=>\s*typeof\(HalloweenAnimations\)/);
    assert.match(theme, /BrandMark\s*=>\s*typeof\(HalloweenBrandMark\)/);
    assert.match(theme, /new\(10,\s*20,\s*11,\s*2\)/, 'halloween runs from 20 October to 2 November');
    assert.ok(brandMark.includes('fc-halloween-logo') && brandMark.includes('fc-pumpkin'));
});

test('appearance is initialized before the first render', () => {
    const init = program.indexOf('appearance.InitializeAsync()');
    const run = program.indexOf('host.RunAsync()');
    assert.ok(init > 0 && run > 0 && init < run, 'appearance must be initialized before host.RunAsync()');
});

test('halloween component disposes both timers', () => {
    assert.match(razor, /@implements\s+IDisposable/);
    assert.match(razor, /_spiderTimer\?\.Dispose\(\)/);
    assert.match(razor, /_batsTimer\?\.Dispose\(\)/);
});

test('dashboard-content.css cache version is bumped', () => {
    const match = index.match(/dashboard-content\.css\?v=(\d+)/);
    assert.ok(match, 'dashboard-content.css must be referenced with a cache-busting version');
    assert.ok(Number(match[1]) >= 12, `cache version must be >= 12, got ${match[1]}`);
});
