window.profileResultNavigation = (() => {
    const positions = new Map();
    const selectors = '.fc-results-panel .mud-table-container, .fc-results-panel .fc-table-wrap';
    return {
        capture(key) { positions.set(key, { x: window.scrollX, y: window.scrollY,
            tables: [...document.querySelectorAll(selectors)].map(e => ({ x: e.scrollLeft, y: e.scrollTop })) }); },
        restore(key) {
            const saved = positions.get(key); if (!saved) return;
            requestAnimationFrame(() => requestAnimationFrame(() => {
                window.scrollTo(saved.x, saved.y);
                [...document.querySelectorAll(selectors)].forEach((e, i) => {
                    const position = saved.tables[i]; if (position) { e.scrollLeft = position.x; e.scrollTop = position.y; }
                });
                positions.delete(key);
            }));
        }
    };
})();
