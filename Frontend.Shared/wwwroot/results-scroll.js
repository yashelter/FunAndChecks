// Вторая полоса горизонтальной прокрутки над шапкой таблицы результатов (.fc-results-hscroll).
// Полоса закреплена у верха окна: прокручивать задачи вбок можно, не спускаясь к нижнему краю таблицы.
// attach(host, options) идемпотентен: компонент вызывает его после рендера, когда сменились данные или колонки.
// Здесь же — один обработчик на всю таблицу вместо обработчика в каждой ячейке (их тысячи):
//  - клик по ячейке задачи в таблице преподавателя → options.dotnet.OnCellClickFromJs(студент, задача);
//  - подсказка и aria-label ячейки ставятся при наведении и фокусе, а не рендерятся в каждую ячейку.
// Студент — data-s у строки, задача — data-t у ячейки шапки в той же колонке.
window.resultsScroll = (() => {
    const attached = new WeakMap();

    function cellInfo(table, target) {
        const mark = target?.closest?.('.fc-result-cell');
        const cell = mark?.closest('td');
        const row = cell?.parentElement;
        const head = table.querySelector('thead tr')?.cells?.[cell?.cellIndex];
        if (!mark || !row?.dataset?.s || !head?.dataset?.t) return null;
        return { mark, row, head, studentId: row.dataset.s, taskId: Number(head.dataset.t) };
    }

    function describe(info, options) {
        const { mark, row, head } = info;
        const status = mark.classList.contains('accepted') || mark.classList.contains('fc-result-accepted') ? options.accepted
            : mark.classList.contains('rejected') || mark.classList.contains('fc-result-rework') ? options.rejected
            : options.none;
        const task = head.querySelector('.fc-results-task-title')?.title ?? head.textContent.trim();
        if (options.action) {
            const student = row.querySelector('.fc-results-name')?.title ?? '';
            mark.title = options.action;
            mark.setAttribute('aria-label', `${student}, ${task}: ${status}. ${options.action}`);
        } else {
            mark.title = status ?? '';
            mark.setAttribute('aria-label', `${task}: ${status} ${mark.textContent.trim()}`);
        }
    }

    function bind(host, options) {
        const bar = host.querySelector('.fc-results-hscroll');
        const table = host.querySelector('.mud-table-container');
        if (!bar || !table) return null;
        const inner = bar.firstElementChild;

        // Ширина полосы и таблицы равны, но у таблицы может быть вертикальный скроллбар: диапазоны выравниваем.
        const update = () => {
            const overflow = table.scrollWidth - table.clientWidth;
            bar.hidden = overflow <= 1;
            inner.style.width = `${bar.clientWidth + Math.max(overflow, 0)}px`;
            if (Math.abs(bar.scrollLeft - table.scrollLeft) > 1) bar.scrollLeft = table.scrollLeft;
        };
        // Без флагов: значение переносится, только если отличается, поэтому встречное событие ничего не меняет.
        const fromBar = () => { if (Math.abs(table.scrollLeft - bar.scrollLeft) > 1) table.scrollLeft = bar.scrollLeft; };
        const fromTable = () => { if (Math.abs(bar.scrollLeft - table.scrollLeft) > 1) bar.scrollLeft = table.scrollLeft; };

        // Тексты меняются при каждом показе, а не кешируются в ячейке: после живого обновления статус мог смениться.
        const state = { options: options ?? {} };
        const onHint = event => { const info = cellInfo(table, event.target); if (info) describe(info, state.options); };
        const onClick = event => {
            const dotnet = state.options.dotnet;
            const info = dotnet && cellInfo(table, event.target);
            if (info) dotnet.invokeMethodAsync('OnCellClickFromJs', info.studentId, info.taskId);
        };

        bar.addEventListener('scroll', fromBar, { passive: true });
        table.addEventListener('scroll', fromTable, { passive: true });
        table.addEventListener('click', onClick);
        table.addEventListener('pointerover', onHint, { passive: true });
        table.addEventListener('focusin', onHint, { passive: true });
        const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(update) : null;
        observer?.observe(table);
        const content = table.querySelector('table');
        if (content) observer?.observe(content);
        update();

        return {
            table,
            update,
            setOptions(next) { state.options = next ?? {}; },
            dispose() {
                bar.removeEventListener('scroll', fromBar);
                table.removeEventListener('scroll', fromTable);
                table.removeEventListener('click', onClick);
                table.removeEventListener('pointerover', onHint);
                table.removeEventListener('focusin', onHint);
                observer?.disconnect();
            }
        };
    }

    return {
        attach(host, options) {
            if (!host) return;
            const current = attached.get(host);
            if (current && current.table === host.querySelector('.mud-table-container')) { current.setOptions(options); current.update(); return; }
            current?.dispose();
            const binding = bind(host, options);
            if (binding) attached.set(host, binding); else attached.delete(host);
        },
        detach(host) {
            if (!host) return;
            attached.get(host)?.dispose();
            attached.delete(host);
        },
        // «К моей строке»: строки виртуализированы, нужной может не быть в DOM. Сначала прокручиваем область
        // таблицы по номеру строки (index × высота строки), затем, когда строка отрисована, ставим её в центр
        // и переводим фокус на ссылку с именем — чтобы с клавиатуры можно было продолжить с этого места.
        scrollToRow(host, index, rowHeight) {
            const table = host?.querySelector('.mud-table-container');
            if (!table) return;
            const smooth = !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            host.scrollIntoView({ block: 'start', behavior: smooth ? 'smooth' : 'auto' });
            const frame = () => new Promise(resolve => requestAnimationFrame(resolve));
            const header = table.querySelector('thead')?.offsetHeight ?? 0;
            (async () => {
                // Кадр на отрисовку после сброса фильтра: иначе высота содержимого ещё старая и scrollTop обрежется.
                await frame();
                const visible = table.clientHeight - header;
                table.scrollTop = Math.max(0, index * rowHeight - Math.max(0, (visible - rowHeight) / 2));
                for (let attempt = 0; attempt < 10; attempt++) {
                    await frame();
                    const row = table.querySelector('tr.fc-results-me');
                    if (!row) continue;
                    const offset = row.offsetTop - header - Math.max(0, (visible - row.offsetHeight) / 2);
                    if (Math.abs(table.scrollTop - offset) > 2) table.scrollTop = Math.max(0, offset);
                    row.querySelector('.fc-results-name a')?.focus({ preventScroll: true });
                    return;
                }
            })();
        }
    };
})();
