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
        }
    };
})();
