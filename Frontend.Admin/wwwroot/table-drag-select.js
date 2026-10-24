// Выбор строк протяжкой: нажать на галочку строки и, не отпуская, провести по соседним.
// Все пройденные строки получают то же состояние, что и первая после нажатия (отметить или снять).
// Состояние меняется click() по галочке, поэтому MudTable узнаёт о выборе как от пользователя.

const checkboxOf = row => row?.querySelector('td:first-child input[type="checkbox"]') ?? null;

export function init(host) {
    if (!host || host.dataset.dragSelect === "on") return;
    host.dataset.dragSelect = "on";
    let drag = null;
    // Первую строку переключаем сами уже при нажатии: родной click браузер пришлёт, только если кнопку
    // отпустили на той же галочке, и он переключил бы строку второй раз — его гасим.
    let swallowRow = null;

    const rowAt = (x, y) => {
        const row = document.elementFromPoint(x, y)?.closest("tr");
        return row && host.contains(row) && row.closest("tbody") ? row : null;
    };

    const visit = row => {
        if (!row || drag.visited.has(row)) return;
        drag.visited.add(row);
        const box = checkboxOf(row);
        if (box && !box.disabled && box.checked !== drag.target) box.click();
    };

    const track = e => {
        if (!drag || e.pointerId !== drag.pointerId) return;
        const row = rowAt(e.clientX, e.clientY);
        if (!row || row === drag.last) return;
        // Строки, проскочившие между событиями при быстром движении, тоже отмечаются.
        const rows = [...host.querySelectorAll("tbody tr")];
        const from = rows.indexOf(drag.last), to = rows.indexOf(row);
        if (from >= 0 && to >= 0)
            for (let i = Math.min(from, to); i <= Math.max(from, to); i++) visit(rows[i]);
        drag.last = row;
    };

    const end = e => {
        if (!drag) return;
        if (e.type === "pointerup") track(e);
        document.removeEventListener("pointermove", track);
        document.removeEventListener("pointerup", end);
        document.removeEventListener("pointercancel", end);
        host.classList.remove("fc-drag-selecting");
        drag = null;
        // click (если будет) приходит сразу после pointerup, раньше этого таймера.
        setTimeout(() => { swallowRow = null; }, 0);
    };

    host.addEventListener("click", e => {
        if (swallowRow && e.isTrusted && swallowRow.contains(e.target) && e.target.closest("td:first-child")) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);

    host.addEventListener("pointerdown", e => {
        if (e.button !== 0 || !e.target.closest("tbody td:first-child")) return;
        const row = e.target.closest("tr");
        const box = checkboxOf(row);
        if (!box || box.disabled) return;
        drag = { pointerId: e.pointerId, target: !box.checked, last: row, visited: new Set([row]) };
        box.click();
        swallowRow = row;
        host.classList.add("fc-drag-selecting");
        document.addEventListener("pointermove", track);
        document.addEventListener("pointerup", end);
        document.addEventListener("pointercancel", end);
    });
}
