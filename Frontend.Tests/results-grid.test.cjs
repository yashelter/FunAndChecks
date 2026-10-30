const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const css = read('Frontend.Shared/wwwroot/dashboard-content.css');
const adminCss = read('Frontend.Admin/wwwroot/admin-dashboard.css');
const columns = read('Frontend.Shared/UI/ResultsColumns.cs');
const adminGrid = read('Frontend.Admin/UI/AdminResultsGrid.razor') + read('Frontend.Admin/UI/AdminResultsGrid.razor.cs');
const sharedGrid = read('Frontend.Shared/UI/SubjectResultsView.razor');
const index = read('Frontend/wwwroot/index.html');
const scrollJs = read('Frontend.Shared/wwwroot/results-scroll.js');

// --- Стили и разметка таблицы результатов ---

test('virtualized row height in CSS matches ResultsColumns.RowHeight', () => {
    const rowHeight = Number(columns.match(/RowHeight\s*=\s*(\d+)/)[1]);
    const cssRow = Number(css.match(/--fc-results-row:\s*(\d+)px/)[1]);
    assert.equal(cssRow, rowHeight, 'ItemSize and the CSS row height must agree, otherwise virtual scrolling jumps');
    assert.match(css, /\.mud-table-body \.mud-table-row > \.mud-table-cell \{[^}]*height:var\(--fc-results-row\)/);
});

test('both results grids are virtualized with a fixed header and their own scroll area', () => {
    for (const [name, grid] of [['admin', adminGrid], ['shared', sharedGrid]]) {
        assert.match(grid, /Virtualize="true"/, `${name} grid must be virtualized`);
        assert.match(grid, /ItemSize="ResultsColumns\.RowHeight"/, `${name} grid must use the shared row height`);
        assert.match(grid, /FixedHeader="true"/, `${name} grid needs a fixed header`);
        assert.match(grid, /class="fc-results-hscroll"/, `${name} grid needs the top scrollbar`);
        assert.match(grid, /resultsScroll\.attach/, `${name} grid must attach the top scrollbar`);
        assert.match(grid, /resultsScroll\.detach/, `${name} grid must detach the top scrollbar`);
        assert.match(grid, /CellClass="fc-results-name-column"/);
        assert.match(grid, /fc-results-total-column/);
    }
    // Своя область прокрутки: высота ограничена сверху, маленькая таблица не растягивается на весь экран.
    assert.match(css, /\.fc-results-grid \.mud-table-container \{[^}]*max-height:var\(--fc-results-height\)/);
});

test('name and total stay pinned when scrolling sideways', () => {
    // Правило с position:sticky: у колонок есть и другие правила (например, размер шрифта на телефоне).
    const rule = selector => [...css.matchAll(new RegExp(`${selector.replace(/[.]/g, '\\.')} \\{([^}]*)\\}`, 'g'))]
        .map(m => m[1]).find(body => /position:sticky/.test(body));
    const name = rule('.fc-results-grid .fc-results-name-column');
    const total = rule('.fc-results-grid .fc-results-total-column');
    assert.ok(name && total, 'sticky rules for the name and Σ columns are missing');
    assert.match(name, /position:sticky; left:0/);
    assert.match(name, /width:var\(--fc-results-name\)/);
    assert.match(total, /position:sticky; left:var\(--fc-results-name\)/, 'Σ sits right after the fixed-width name column');
});

test('many tasks get vertical, truncated headers', () => {
    assert.match(columns, /fc-results-many-tasks/);
    assert.match(css, /\.fc-results-many-tasks \.fc-results-task-title \{[^}]*writing-mode:vertical-rl/);
    for (const grid of [adminGrid, sharedGrid])
        assert.match(grid, /<span class="fc-results-task-title" title="@task\.TaskName">/, 'full task name stays in the tooltip');
});

test('teacher colors toggle by a class, without re-rendering the grid', () => {
    assert.match(css, /\.fc-results-show-teacher \.fc-result-cell\[style\*="--tc"\]/);
    for (const grid of [read('Frontend.Admin/UI/AdminResultsGrid.razor.cs'), sharedGrid])
        assert.match(grid, /--tc:\{cell\.AdminColor\}/);
    assert.doesNotMatch(read('Frontend.Admin/UI/AdminResultsGrid.razor.cs'), /ShowTeacherColors/);
});

test('the results panel does not clip sticky elements into its own scroll box', () => {
    assert.match(adminCss, /\.fc-results-panel\{min-width:0;overflow:clip\}/);
    assert.match(css, /\.fc-results-hscroll \{[^}]*position:sticky; top:0/);
});

test('changed stylesheets and the new script are referenced with fresh versions', () => {
    assert.ok(Number(index.match(/dashboard-content\.css\?v=(\d+)/)[1]) >= 14);
    assert.match(index, /admin-dashboard\.css\?v=ux-7/);
    assert.match(index, /_content\/Frontend\.Shared\/results-scroll\.js/);
});

// --- results-scroll.js в маленьком фейковом DOM ---

function element(props = {}) {
    const listeners = {};
    return Object.assign({
        scrollLeft: 0, scrollWidth: 0, clientWidth: 0, hidden: false, style: {},
        addEventListener(type, fn) { (listeners[type] ??= []).push(fn); },
        removeEventListener(type, fn) { listeners[type] = (listeners[type] ?? []).filter(f => f !== fn); },
        fire(type) { for (const fn of listeners[type] ?? []) fn(); },
        listenerCount: type => (listeners[type] ?? []).length,
    }, props);
}

function setup({ tableWidth = 3000, viewWidth = 1000, scrollbar = 15 } = {}) {
    const inner = element();
    const bar = element({ clientWidth: viewWidth, firstElementChild: inner });
    const content = element();
    const table = element({ scrollWidth: tableWidth, clientWidth: viewWidth - scrollbar, querySelector: () => content });
    const host = { querySelector: s => s === '.fc-results-hscroll' ? bar : s === '.mud-table-container' ? table : null };
    const observers = [];
    class ResizeObserver { constructor(cb) { this.cb = cb; this.targets = []; this.disconnected = false; observers.push(this); }
        observe(t) { this.targets.push(t); } disconnect() { this.disconnected = true; } }
    const window = {};
    vm.runInNewContext(scrollJs, { window, ResizeObserver });
    return { api: window.resultsScroll, host, bar, inner, table, observers };
}

test('top scrollbar covers the same scroll range as the table', () => {
    const { api, host, bar, inner } = setup({ tableWidth: 3000, viewWidth: 1000, scrollbar: 15 });
    api.attach(host);
    assert.equal(bar.hidden, false);
    // Диапазон таблицы: 3000 − 985 = 2015; у полосы тот же: ширина содержимого 1000 + 2015.
    assert.equal(inner.style.width, '3015px');
});

test('scrolling either bar moves the other, without ping-pong', () => {
    const { api, host, bar, table } = setup();
    api.attach(host);
    bar.scrollLeft = 400; bar.fire('scroll');
    assert.equal(table.scrollLeft, 400);
    table.fire('scroll'); // ответное событие ничего не меняет
    assert.equal(bar.scrollLeft, 400);
    table.scrollLeft = 900; table.fire('scroll');
    assert.equal(bar.scrollLeft, 900);
});

test('the top scrollbar hides when the table fits and follows width changes', () => {
    const { api, host, bar, table, observers } = setup({ tableWidth: 900, viewWidth: 1000, scrollbar: 0 });
    api.attach(host);
    assert.equal(bar.hidden, true);
    table.scrollWidth = 2500;
    observers[0].cb();
    assert.equal(bar.hidden, false);
});

test('attach is idempotent and detach removes the listeners', () => {
    const { api, host, bar, table, observers } = setup();
    api.attach(host);
    api.attach(host);
    assert.equal(bar.listenerCount('scroll'), 1, 'repeated attach must not add listeners');
    api.detach(host);
    assert.equal(bar.listenerCount('scroll'), 0);
    assert.equal(table.listenerCount('scroll'), 0);
    assert.ok(observers.every(o => o.disconnected));
});
