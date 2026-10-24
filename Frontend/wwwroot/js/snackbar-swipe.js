// Уведомления (MudBlazor snackbar) можно смахнуть влево или вправо — пальцем или мышью.
// Закрываем крестиком самого уведомления, чтобы Blazor узнал об этом как о ручном закрытии.
(() => {
    const threshold = 80;
    let drag = null;
    let suppressClickUntil = 0;

    const closeButton = snack => snack.querySelector('.mud-snackbar-close-button');

    document.addEventListener('pointerdown', e => {
        const snack = e.target.closest?.('.mud-snackbar');
        if (!snack || e.button !== 0 || e.target.closest('button, a')) return;
        drag = { snack, id: e.pointerId, x: e.clientX, dx: 0 };
    }, { passive: true });

    document.addEventListener('pointermove', e => {
        if (!drag || e.pointerId !== drag.id) return;
        drag.dx = e.clientX - drag.x;
        if (Math.abs(drag.dx) < 6) return;
        drag.snack.style.transition = 'none';
        drag.snack.style.transform = `translateX(${drag.dx}px)`;
        drag.snack.style.opacity = String(Math.max(0.2, 1 - Math.abs(drag.dx) / 240));
    }, { passive: true });

    const end = e => {
        if (!drag || e.pointerId !== drag.id) return;
        const { snack, dx } = drag;
        drag = null;
        if (Math.abs(dx) >= 6) suppressClickUntil = Date.now() + 400; // клик после смахивания не должен нажать «Отменить»
        snack.style.transition = 'transform .18s ease, opacity .18s ease';
        const button = Math.abs(dx) >= threshold ? closeButton(snack) : null;
        if (!button) { snack.style.transform = ''; snack.style.opacity = ''; return; }
        snack.style.transform = `translateX(${Math.sign(dx) * 120}%)`;
        snack.style.opacity = '0';
        setTimeout(() => button.click(), 160);
    };
    document.addEventListener('pointerup', end);
    document.addEventListener('pointercancel', end);

    window.addEventListener('click', e => {
        if (Date.now() < suppressClickUntil && e.isTrusted && e.target.closest?.('.mud-snackbar')) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);
})();
