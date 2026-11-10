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
// Общая таблица: разметка с шапкой и виртуализацией, её код и компонент строки.
const table = read('Frontend.Shared/UI/ResultsTable.razor');
const tableCode = read('Frontend.Shared/UI/ResultsTable.razor.cs');
const row = read('Frontend.Shared/UI/ResultsRow.razor');
const index = read('Frontend/wwwroot/index.html');
const scrollJs = read('Frontend.Shared/wwwroot/results-scroll.js');

// --- Стили и разметка таблицы результатов ---

test('virtualized row height in CSS matches ResultsColumns.RowHeight', () => {
    const rowHeight = Number(columns.match(/RowHeight\s*=\s*(\d+)/)[1]);
    const cssRow = Number(css.match(/--fc-results-row:\s*(\d+)px/)[1]);
    assert.equal(cssRow, rowHeight, 'ItemSize and the CSS row height must agree, otherwise virtual scrolling jumps');
    assert.match(css, /\.mud-table-body \.mud-table-row > \.mud-table-cell \{[^}]*height:var\(--fc-results-row\)/);
});

test('both cabinets use the one lightweight table instead of MudDataGrid', () => {
    for (const [name, grid] of [['admin', adminGrid], ['shared', sharedGrid]]) {
        assert.match(grid, /<ResultsTable /, `${name} results must be shown by ResultsTable`);
        assert.doesNotMatch(grid, /<MudDataGrid/, `${name} results must not go back to MudDataGrid`);
    }
    assert.doesNotMatch(table + row, /<MudDataGrid|<TemplateColumn|<PropertyColumn/);
});

test('the table is virtualized with a fixed header and its own scroll area', () => {
    assert.match(table, /<Virtualize Items="_visible" ItemSize="ResultsColumns\.RowHeight"[^>]*SpacerElement="tr"/);
    assert.match(table, /mud-table-sticky-header/, 'the header stays pinned through the MudBlazor sticky-header rule');
    assert.match(table, /class="mud-table-container"/, 'results-scroll.js and profile-navigation.js find the scroll area by this class');
    assert.match(table, /class="fc-results-hscroll"/, 'the table needs the top scrollbar');
    assert.match(tableCode, /resultsScroll\.attach/);
    assert.match(tableCode, /resultsScroll\.detach/);
    assert.match(row, /class="mud-table-cell fc-results-name-column"/);
    assert.match(row, /fc-results-total-column/);
    // Своя область прокрутки: высота ограничена сверху, маленькая таблица не растягивается на весь экран.
    assert.match(css, /\.fc-results-grid \.mud-table-container \{[^}]*max-height:var\(--fc-results-height\)/);
});

test('scrolling re-renders only rows whose data changed', () => {
    // Virtualize на каждом шаге прокрутки обходит все видимые строки: без этой проверки рендерились бы тысячи ячеек.
    assert.match(row, /ShouldRender\(\) => !ReferenceEquals\(_shownRow, Row\) \|\| !ReferenceEquals\(_shownLayout, Layout\)/);
    assert.match(table, /<ResultsRow @key="row\.StudentId" Row="row" Layout="_layout" \/>/);
    assert.match(tableCode, /protected override bool ShouldRender\(\) => _dirty;/);
});

test('task cells carry no handlers or tooltips of their own', () => {
    // Единственный обработчик строки — ссылка на профиль; клик по ячейке и подсказки делегированы в results-scroll.js.
    assert.deepEqual(row.match(/@onclick="[^"]*"/g), ['@onclick="OpenProfileAsync"']);
    const cells = row.slice(row.indexOf('@foreach (var task in Layout.Tasks)'), row.indexOf('@foreach (var column in Layout.Grades)'));
    assert.doesNotMatch(cells, /title=|aria-label=|data-/);
    assert.match(table, /class="mud-table-cell fc-results-task-header" data-t="@task\.TaskId"/);
    assert.match(row, /<tr class="@RowClass\(\)" data-s="@Row\.StudentId">/);
    assert.match(tableCode, /\[JSInvokable\]\s*public Task OnCellClickFromJs\(string studentId, int taskId\)/);
});

test('name and total stay pinned when scrolling sideways', () => {
    // Правило с position:sticky: у колонок есть и другие правила (например, отступы на телефоне).
    const rule = selector => [...css.matchAll(new RegExp(`${selector.replace(/[.]/g, '\\.')} \\{([^}]*)\\}`, 'g'))]
        .map(m => m[1]).find(body => /position:sticky/.test(body));
    const name = rule('.fc-results-grid .fc-results-name-column');
    const total = rule('.fc-results-grid .fc-results-total-column');
    assert.ok(name && total, 'sticky rules for the name and Σ columns are missing');
    // «Место» — первая закреплённая колонка точной ширины, имя и Σ сдвинуты на неё.
    assert.match(name, /position:sticky; left:var\(--fc-results-rank\)/);
    assert.match(name, /width:var\(--fc-results-name\)/);
    assert.match(total, /position:sticky; left:calc\(var\(--fc-results-name\) \+ var\(--fc-results-rank\)\)/,
        'Σ sits right after the fixed-width rank and name columns');
    const rank = rule('.fc-results-grid .mud-table-root .mud-table-row > .mud-table-cell.fc-results-rank-column');
    assert.ok(rank, 'sticky rule for the rank column is missing');
    assert.match(rank, /position:sticky; left:0/);
    assert.match(rank, /width:var\(--fc-results-rank\); min-width:var\(--fc-results-rank\); max-width:var\(--fc-results-rank\)/);
    // Без колонки (публичная таблица) ширина нулевая и имя стоит у края.
    assert.match(css, /\.fc-results-grid \{[^}]*--fc-results-rank:0px/);
    assert.match(css, /\.fc-results-grid\.fc-results-ranked \{ --fc-results-rank:\d+px; \}/);
});

test('rank column is in both cabinets, the public table keeps its columns', () => {
    assert.match(adminGrid, /<ResultsTable [^>]*ShowRank="true"/);
    assert.match(sharedGrid, /<ResultsTable [^>]*ShowRank="ShowRank"/);
    assert.match(table, /@if \(ShowRank\)\s*\{[\s\S]*?class="mud-table-cell fc-results-rank-column"/);
    assert.match(row, /@if \(Layout\.ShowRank\)\s*\{\s*<td class="mud-table-cell fc-results-rank-column">/);
    assert.match(table, /GridClass\(Results, ShowRank\)/);
    assert.match(read('Frontend.Student/Pages/Dashboard.razor'), /ShowRank="true"/);
    assert.doesNotMatch(read('Frontend/Pages/PublicResults.razor'), /ShowRank/);
});

test('own row gets a "you" label inside the pinned name cell', () => {
    assert.match(row, /@if \(IsMe\)\s*\{\s*<span class="fc-results-you">/);
    const community = read('Frontend.Shared/wwwroot/community.css');
    assert.match(community, /\.fc-results-you\{flex-shrink:0;/);
    assert.match(community, /\.fc-results-me td\.fc-results-name-column,[^{]*\{background:color-mix/);
});

test('many tasks get vertical, truncated headers', () => {
    assert.match(columns, /fc-results-many-tasks/);
    assert.match(css, /\.fc-results-many-tasks \.fc-results-task-title \{[^}]*writing-mode:vertical-rl/);
    assert.match(table, /<span class="fc-results-task-title" title="@task\.TaskName">/, 'full task name stays in the tooltip');
});

test('teacher colors toggle by a class, without re-rendering the grid', () => {
    assert.match(css, /\.fc-results-show-teacher \.fc-result-cell\[style\*="--tc"\]/);
    assert.equal(row.match(/--tc:\{cell\.AdminColor\}/g).length, 2, 'both the student and the teacher cell keep the color in --tc');
    assert.doesNotMatch(adminGrid + tableCode, /ShowTeacherColors/);
});

test('on a phone only the rank and a short name stay pinned', () => {
    // Блок телефона для таблицы — тот @media, в котором включается короткое имя (в файле есть и другие).
    const phoneStart = css.lastIndexOf('@media (max-width:600px)', css.indexOf('.fc-results-grid .fc-name-short { display:inline'));
    assert.ok(phoneStart >= 0, 'the phone block of the results table is missing');
    const phone = css.slice(phoneStart, css.indexOf('\n}', phoneStart));
    const px = name => Number(phone.match(new RegExp(`--fc-results-${name}:(\\d+)px`))[1]);
    assert.ok(px('name') + px('rank') <= 160, 'the pinned block must leave most of a 360px screen to the tasks');
    assert.match(phone, /\.mud-table-cell\.fc-results-total-column,\s*[^{]*\.mud-table-cell\.fc-results-group-column \{ display:none; \}/);
    assert.match(phone, /\.fc-results-grid \.fc-name-short \{ display:inline; \}/);
    assert.match(phone, /\.fc-results-grid \.fc-results-sum \{ display:inline;/);
    // Вне телефона короткое имя и Σ в ячейке имени скрыты.
    assert.match(css, /\.fc-name-short, \.fc-results-sum \{ display:none; \}/);
    assert.match(row, /<span class="fc-name-short">@ResultsColumns\.ShortName\(Row\.FullName\)<\/span>/);
    assert.match(row, /<span class="fc-results-sum" aria-hidden="true">@Row\.TotalPoints<\/span>/);
    // Блок телефона стоит после правил «ведомости», иначе её размер ячейки его перебьёт.
    assert.ok(phoneStart > css.indexOf('.fc-results-journal .fc-result-cell {'));
});

test('the results panel does not clip sticky elements into its own scroll box', () => {
    assert.match(adminCss, /\.fc-results-panel\{min-width:0;overflow:clip\}/);
    assert.match(css, /\.fc-results-hscroll \{[^}]*position:sticky; top:0/);
});

test('changed stylesheets and the new script are referenced with fresh versions', () => {
    assert.ok(Number(index.match(/dashboard-content\.css\?v=(\d+)/)[1]) >= 18);
    assert.ok(Number(index.match(/community\.css\?v=(\d+)/)[1]) >= 11);
    assert.ok(Number(index.match(/admin-dashboard\.css\?v=ux-(\d+)/)[1]) >= 9);
    assert.ok(Number(index.match(/_content\/Frontend\.Shared\/results-scroll\.js\?v=(\d+)/)[1]) >= 3);
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
    assert.equal(table.listenerCount('click'), 1, 'repeated attach must not add a second click handler');
    api.detach(host);
    assert.equal(bar.listenerCount('scroll'), 0);
    assert.equal(table.listenerCount('scroll'), 0);
    for (const type of ['click', 'pointerover', 'focusin'])
        assert.equal(table.listenerCount(type), 0, `${type} listener must be removed`);
    assert.ok(observers.every(o => o.disconnected));
});

test('scrollToRow scrolls a virtualized table by row index, then centers and focuses the own row', async () => {
    const link = { focused: null, focus(options) { this.focused = options; } };
    const row = { offsetTop: 0, offsetHeight: 37, querySelector: () => link };
    let rendered = false;
    const table = {
        clientHeight: 437, scrollTop: 0,
        querySelector: s => s === 'thead' ? { offsetHeight: 40 } : s === 'tr.fc-results-me' && rendered ? row : null,
    };
    const host = { scrolled: null, scrollIntoView(o) { this.scrolled = o; }, querySelector: s => s === '.mud-table-container' ? table : null };
    const frames = [];
    const window = { matchMedia: () => ({ matches: true }) };
    vm.runInNewContext(scrollJs, { window, ResizeObserver: undefined, requestAnimationFrame: cb => frames.push(cb) });
    const flush = async () => { while (frames.length) { frames.shift()(); await new Promise(r => setImmediate(r)); } };

    window.resultsScroll.scrollToRow(host, 100, 37);
    assert.equal(host.scrolled.behavior, 'auto', 'reduced motion: no smooth scrolling');
    frames.shift()(); await new Promise(r => setImmediate(r));
    // 100 строк × 37 px минус половина видимой области под шапкой: строка ещё не в DOM, но окажется на экране.
    assert.equal(table.scrollTop, 100 * 37 - (437 - 40 - 37) / 2);
    rendered = true;
    row.offsetTop = 40 + 100 * 37;
    await flush();
    assert.equal(table.scrollTop, 100 * 37 - (437 - 40 - 37) / 2);
    assert.equal(link.focused?.preventScroll, true, "focus must not scroll the page again");
});

// --- Делегированный клик и подсказки ячеек ---

function cellTable() {
    const listeners = {};
    const head = { dataset: { t: '42' }, textContent: 'Лаба 1', querySelector: s => s === '.fc-results-task-title' ? { title: 'Лабораторная 1' } : null };
    const tr = { dataset: { s: 'student-1' }, querySelector: s => s === '.fc-results-name' ? { title: 'Иванов Иван' } : null };
    const td = { cellIndex: 4, parentElement: tr };
    const mark = (classes, text) => {
        const attrs = {};
        const el = { title: '', textContent: text, attrs, classList: { contains: c => classes.includes(c) },
            setAttribute(k, v) { attrs[k] = v; }, closest: s => s === '.fc-result-cell' ? el : s === 'td' ? td : null };
        return el;
    };
    const table = {
        scrollLeft: 0, scrollWidth: 2000, clientWidth: 500,
        addEventListener(type, fn) { (listeners[type] ??= []).push(fn); },
        removeEventListener(type, fn) { listeners[type] = (listeners[type] ?? []).filter(f => f !== fn); },
        fire(type, target) { for (const fn of listeners[type] ?? []) fn({ target }); },
        querySelector: s => s === 'thead tr' ? { cells: { 4: head } } : null,
    };
    const bar = element({ clientWidth: 500, firstElementChild: element() });
    const host = { querySelector: s => s === '.fc-results-hscroll' ? bar : s === '.mud-table-container' ? table : null };
    const window = {};
    vm.runInNewContext(scrollJs, { window, ResizeObserver: undefined });
    return { api: window.resultsScroll, host, table, mark };
}

test('one delegated click on the table opens the student on the clicked task', () => {
    const { api, host, table, mark } = cellTable();
    const calls = [];
    const dotnet = { invokeMethodAsync: (...args) => calls.push(args) };
    api.attach(host, { dotnet, accepted: 'Зачтено', rejected: 'На доработке', none: 'Не сдано', action: 'Открыть' });

    table.fire('click', mark(['fc-result-cell', 'fc-result-accepted'], '+'));
    assert.deepEqual(calls, [['OnCellClickFromJs', 'student-1', 42]]);
    // Клик мимо ячейки задачи (имя, шапка, пустое место) ничего не открывает.
    table.fire('click', { closest: () => null });
    assert.equal(calls.length, 1);

    api.detach(host);
    table.fire('click', mark(['fc-result-cell'], ''));
    assert.equal(calls.length, 1, 'detach removes the click listener');
});

test('the student table has no click target, cells are described on hover and focus', () => {
    const { api, host, table, mark } = cellTable();
    api.attach(host, { dotnet: null, accepted: 'Зачтено', rejected: 'На доработке', none: 'Не сдано', action: null });
    const cell = mark(['fc-result-cell', 'rejected'], 'Д');
    table.fire('click', cell); // без dotnet клик не падает и никуда не идёт
    table.fire('pointerover', cell);
    assert.equal(cell.title, 'На доработке');
    assert.equal(cell.attrs['aria-label'], 'Лабораторная 1: На доработке Д');

    // Повторный attach той же таблицы меняет тексты (смена языка), не добавляя обработчиков.
    api.attach(host, { dotnet: {}, accepted: 'Accepted', rejected: 'Rework', none: 'Not submitted', action: 'Open student' });
    const teacherCell = mark(['fc-result-cell', 'fc-result-unsubmitted'], '');
    table.fire('focusin', teacherCell);
    assert.equal(teacherCell.title, 'Open student');
    assert.equal(teacherCell.attrs['aria-label'], 'Иванов Иван, Лабораторная 1: Not submitted. Open student');
});
