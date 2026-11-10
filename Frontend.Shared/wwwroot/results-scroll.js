// Вторая полоса горизонтальной прокрутки над шапкой таблицы результатов (.fc-results-hscroll).
// Полоса закреплена у верха окна: прокручивать задачи вбок можно, не спускаясь к нижнему краю таблицы.
// attach(host) идемпотентен: компонент вызывает его после рендера, когда таблица пересоздана с новыми колонками.
window.resultsScroll = (() => {
    const attached = new WeakMap();

    function bind(host) {
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

        bar.addEventListener('scroll', fromBar, { passive: true });
        table.addEventListener('scroll', fromTable, { passive: true });
        const observer = typeof ResizeObserver === 'function' ? new ResizeObserver(update) : null;
        observer?.observe(table);
        const content = table.querySelector('table');
        if (content) observer?.observe(content);
        update();

        return {
            table,
            update,
            dispose() {
                bar.removeEventListener('scroll', fromBar);
                table.removeEventListener('scroll', fromTable);
                observer?.disconnect();
            }
        };
    }

    return {
        attach(host) {
            if (!host) return;
            const current = attached.get(host);
            if (current && current.table === host.querySelector('.mud-table-container')) { current.update(); return; }
            current?.dispose();
            const binding = bind(host);
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
