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

/** Возвращает полный текст блока @keyframes name {...}. */
function keyframesBody(name) {
    const start = css.indexOf(`@keyframes ${name}`);
    assert.ok(start > 0, `missing @keyframes ${name}`);
    const open = css.indexOf('{', start);
    let depth = 0;
    for (let i = open; i < css.length; i++) {
        if (css[i] === '{') depth++;
        else if (css[i] === '}') {
            depth--;
            if (depth === 0) return css.slice(open, i + 1);
        }
    }
    return css.slice(open);
}

test('dashboard-content.css has balanced braces', () => {
    assert.equal(braceBalance(css), 0, 'CSS has unbalanced braces — rules after the break are ignored by the browser');
});

test('halloween animation section is not nested inside an unclosed block', () => {
    // Раньше @media (prefers-reduced-motion:reduce) остался незакрытым, и вся
    // секция Halloween-анимаций оказалась внутри него — в обычном режиме
    // стили не применялись, а три SVG висели как обычные картинки.
    const marker = css.indexOf('Halloween Animations');
    assert.ok(marker > 0, 'Halloween animation section is missing from dashboard-content.css');
    assert.equal(braceBalance(css.slice(0, marker)), 0, 'Halloween section is nested inside an unclosed block');
});

test('halloween rules are not trapped inside a prefers-reduced-motion query', () => {
    // Обход CSS со стеком: если правило оказалось внутри @media
    // (prefers-reduced-motion:reduce), оно не применится в обычном режиме.
    const source = stripComments(css);
    const stack = [];
    const offenders = [];
    let buffer = '';
    const flush = () => {
        const selector = buffer.trim();
        buffer = '';
        if (!selector) return;
        const inReducedMotion = stack.some(s => /prefers-reduced-motion/.test(s));
        const isHalloween = /fc-halloween-(animations|ghost|spider)|@keyframes\s+(ghost-path|spider-up-down)/.test(selector);
        if (inReducedMotion && isHalloween && !/display:\s*none/.test(selector)) {
            offenders.push(selector.replace(/\s+/g, ' ').slice(0, 80));
        }
    };
    for (const ch of source) {
        if (ch === '{') { stack.push(buffer); buffer = ''; }
        else if (ch === '}') { flush(); stack.pop(); }
        else if (ch === ';') { flush(); }
        else buffer += ch;
    }
    assert.deepEqual(offenders, [], 'Halloween rules are trapped inside prefers-reduced-motion');
});

test('halloween keyframes are defined', () => {
    for (const name of ['ghost-path-1', 'ghost-path-2', 'ghost-path-3', 'ghost-path-4', 'ghost-path-5', 'spider-up-down']) {
        assert.ok(css.includes(`@keyframes ${name}`), `missing @keyframes ${name}`);
    }
});

test('there are several ghosts', () => {
    const count = (razor.match(/fc-halloween-ghost-\d/g) || []).length;
    assert.ok(count >= 5, `expected at least 5 ghosts, found ${count}`);
    for (let i = 1; i <= 5; i++) {
        assert.ok(css.includes(`.fc-halloween-ghost-${i} {`), `missing rule for ghost ${i}`);
    }
});

test('every ghost path crosses the whole screen', () => {
    for (let i = 1; i <= 5; i++) {
        const body = keyframesBody(`ghost-path-${i}`);
        // Начало и конец пути — за краями экрана, а середина — в разных зонах.
        assert.match(body, /translate3d\(-100px|translate3d\(calc\(100vw/, `ghost ${i} must start off-screen`);
        assert.match(body, /100vw/, `ghost ${i} must travel across the full width`);
        assert.match(body, /\d+vh/, `ghost ${i} must travel vertically`);
    }
});

test('ghost paths are staggered so they do not move in lockstep', () => {
    const durations = new Set();
    for (let i = 1; i <= 5; i++) {
        const rule = css.match(new RegExp(`\\.fc-halloween-ghost-${i}\\s*\\{[^}]*animation:[^;]*?([\\d.]+)s`));
        assert.ok(rule, `ghost ${i} has no animation duration`);
        durations.add(rule[1]);
    }
    assert.ok(durations.size >= 4, `ghost durations must differ, got ${[...durations].join(', ')}`);
});

test('ghost animation is an infinite loop on a curved path', () => {
    for (let i = 1; i <= 5; i++) {
        assert.match(css, new RegExp(`\\.fc-halloween-ghost-${i}\\s*\\{[^}]*animation:\\s*ghost-path-${i}\\s+[\\d.]+s[^}]*infinite`), `ghost ${i} must loop forever`);
    }
});

test('ghosts start hidden and off-screen so nothing hangs in the layout', () => {
    const block = css.slice(css.indexOf('.fc-halloween-ghost {'), css.indexOf('.fc-halloween-ghost-left {'));
    assert.match(block, /opacity:\s*0/, 'ghosts must be invisible by default');
    for (let i = 1; i <= 5; i++) {
        const body = keyframesBody(`ghost-path-${i}`);
        assert.match(body, /translate3d\(-100px|translate3d\(calc\(100vw/, `ghost ${i} must start off-screen`);
    }
});

test('spider descends from above the viewport and comes back up', () => {
    const frames = keyframesBody('spider-up-down');
    assert.match(frames, /top:\s*-80px/, 'spider must start above the viewport');
    const descend = frames.indexOf('35vh');
    assert.ok(descend > 0, 'spider must descend into the viewport');
    assert.ok(frames.indexOf('-80px', descend) > descend, 'spider must return up off-screen');
});

test('spider becomes visible within the timer interval', () => {
    // Таймер пересоздаёт элемент (@key) и перезапускает анимацию. Видимая фаза
    // обязана укладываться в интервал таймера, иначе паук не покажется никогда.
    const cycle = Number(css.match(/spider-up-down\s+([\d.]+)s/)[1]);
    const timerSeconds = 20;
    const frames = keyframesBody('spider-up-down');
    const visible = [...frames.matchAll(/([\d.]+)%\s*\{[^}]*opacity:\s*([\d.]+)/g)]
        .filter(m => Number(m[2]) > 0)
        .map(m => Number(m[1]));
    assert.ok(visible.length > 0, 'spider must have a visible phase');
    const lastVisible = Math.max(...visible);
    const visibleSeconds = (lastVisible / 100) * cycle;
    assert.ok(visibleSeconds < timerSeconds,
        `visible phase ends at ${visibleSeconds}s but the timer restarts the element every ${timerSeconds}s`);
});

test('halloween component is mounted in the shared workspace shell', () => {
    assert.ok(shell.includes('<HalloweenAnimations />'), 'WorkspaceShell must render <HalloweenAnimations />');
});

test('halloween decorations also cover the sign-in page', () => {
    // AuthLayout — layout страницы входа: там тоже должна быть тыква и анимации.
    assert.ok(authLayout.includes('<HalloweenAnimations />'), 'AuthLayout must render <HalloweenAnimations />');
    assert.ok(authLayout.includes('fc-halloween-logo'), 'AuthLayout must show the pumpkin logo in halloween mode');
});

test('both layouts react to the halloween preference', () => {
    for (const [name, source] of [['WorkspaceShell', shell], ['AuthLayout', authLayout]]) {
        assert.match(source, /Flags\.HalloweenEvent\s*&&\s*Appearance\.IsHalloweenMode/,
            `${name} must gate the halloween look on the flag and the user preference`);
    }
});

test('appearance is initialized before the first render', () => {
    // Иначе первый кадр рисуется с дефолтным состоянием: у выключивших
    // Halloween оформление на миг появляется и исчезает.
    const init = program.indexOf('appearance.InitializeAsync()');
    const run = program.indexOf('host.RunAsync()');
    assert.ok(init > 0, 'Program.cs must initialize the appearance service');
    assert.ok(run > 0, 'Program.cs must start the host');
    assert.ok(init < run, 'appearance must be initialized before host.RunAsync()');
});

test('halloween component is gated by the feature flag and the user preference', () => {
    assert.match(razor, /Flags\.HalloweenEvent\s*&&\s*Appearance\.IsHalloweenMode/);
});

test('halloween component disposes its timer', () => {
    assert.match(razor, /@implements\s+IDisposable/);
    assert.match(razor, /public\s+void\s+Dispose\(\)/);
});

test('reduced motion hides the halloween decorations', () => {
    assert.match(css, /@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{[^}]*\.fc-halloween-animations[^}]*display:\s*none\s*!important/s);
});

test('dashboard-content.css cache version is bumped', () => {
    // Файл отдаётся с ?v=N — без нового значения браузер продолжит
    // использовать закэшированную версию и не увидит новых правил.
    const match = index.match(/dashboard-content\.css\?v=(\d+)/);
    assert.ok(match, 'dashboard-content.css must be referenced with a cache-busting version');
    assert.ok(Number(match[1]) >= 5, `cache version must be >= 5, got ${match[1]}`);
});
