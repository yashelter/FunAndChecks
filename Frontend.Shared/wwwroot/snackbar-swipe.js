(function () {
    const THRESHOLD_PX = 80;
    const THRESHOLD_RATIO = 0.30;
    const FLY_DURATION = 200;
    const TRANSITION = `transform ${FLY_DURATION}ms ease, opacity ${FLY_DURATION}ms ease`;

    let target   = null;
    let startX   = 0;
    let startY   = 0;
    let decided  = false;
    let isHoriz  = false;

    function reset(restoreEl) {
        if (restoreEl && restoreEl.style.transform !== '') {
            restoreEl.style.transition = TRANSITION;
            restoreEl.style.transform  = '';
            restoreEl.style.opacity    = '';
        }
        target  = null;
        decided = false;
        isHoriz = false;
    }

    function onTouchStart(e) {
        if (target) reset(target);

        const el = e.target.closest('.mud-snackbar');
        if (!el || el.dataset.swiping === '1') return;

        target  = el;
        startX  = e.touches[0].clientX;
        startY  = e.touches[0].clientY;
        decided = false;
        isHoriz = false;
        target.style.transition = 'none';
    }

    function onTouchMove(e) {
        if (!target) return;

        const dx = e.touches[0].clientX - startX;
        const dy = e.touches[0].clientY - startY;

        if (!decided) {
            if (Math.abs(dx) < 6 && Math.abs(dy) < 6) return;
            isHoriz = Math.abs(dx) >= Math.abs(dy);
            decided = true;

            if (!isHoriz) {
                reset(target);
                return;
            }
        }

        if (!isHoriz) return;

        const ratio = Math.min(Math.abs(dx) / (target.offsetWidth || 300), 1);
        target.style.transform = `translateX(${dx}px)`;
        target.style.opacity   = String(1 - ratio * 0.45);
    }

    function onTouchEnd(e) {
        if (!target) return;

        if (!decided || !isHoriz) {
            reset(target);
            return;
        }

        const dx        = e.changedTouches[0].clientX - startX;
        const width     = target.offsetWidth || 300;
        const threshold = Math.max(THRESHOLD_PX, width * THRESHOLD_RATIO);

        if (Math.abs(dx) >= threshold) {
            dismiss(target, dx > 0 ? 1 : -1);
        } else {
            reset(target);
        }

        target  = null;
        decided = false;
        isHoriz = false;
    }

    function onTouchCancel() {
        if (!target) return;
        reset(target);
    }

    function dismiss(el, direction) {
        const flyOut = direction * (window.innerWidth + el.offsetWidth + 20);

        el.dataset.swiping = '1';
        el.style.transition = TRANSITION;
        el.style.transform  = `translateX(${flyOut}px)`;
        el.style.opacity    = '0';

        setTimeout(() => {
            delete el.dataset.swiping;
            const btn = el.querySelector('.mud-snackbar-close');
            if (btn) {
                btn.click();
            } else {
                el.style.display = 'none';
            }
        }, FLY_DURATION + 10);
    }

    document.addEventListener('touchstart',  onTouchStart,  { passive: true });
    document.addEventListener('touchmove',   onTouchMove,   { passive: true });
    document.addEventListener('touchend',    onTouchEnd,    { passive: true });
    document.addEventListener('touchcancel', onTouchCancel, { passive: true });

    window.snackbarTagLast = function (uid) {
        return new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    const snackbars = document.querySelectorAll('.mud-snackbar');
                    if (!snackbars.length) { resolve(false); return; }
                    const el = snackbars[snackbars.length - 1];
                    el.dataset.fcUid = uid;
                    resolve(true);
                });
            });
        });
    };

    window.snackbarFadeDismiss = function (index, fadeDuration) {
        const snackbars = document.querySelectorAll('.mud-snackbar');
        const el = snackbars[index];
        if (!el) return;

        el.style.transition = `opacity ${fadeDuration}ms ease`;
        el.style.opacity = '0';

        setTimeout(() => {
            const btn = el.querySelector('.mud-snackbar-close');
            if (btn) btn.click();
        }, fadeDuration);
    };

    window.snackbarFadeDismissByUid = function (uid, fadeDuration) {
        const el = document.querySelector(`.mud-snackbar[data-fc-uid="${uid}"]`);
        if (!el) return;

        el.style.transition = `opacity ${fadeDuration}ms ease`;
        el.style.opacity = '0';

        setTimeout(() => {
            const btn = el.querySelector('.mud-snackbar-close');
            if (btn) btn.click();
        }, fadeDuration);
    };
})();
