// Цветовое колесо (ColorWheelPicker): перетаскивание по кругу → оттенок (угол от верха по часовой) и насыщенность (доля радиуса).
// Вызовы в .NET не чаще кадра и не параллельно — как в student-color-picker.js.
const instances = new WeakMap();

export function attach(surface, reference) {
    detach(surface);
    let pointer = null, frame = 0, pending = null, sending = false, disposed = false;
    const disabled = () => surface.dataset.disabled === 'true';
    const position = event => {
        const rect = surface.getBoundingClientRect();
        const radius = rect.width / 2;
        if (radius <= 0) return;
        const dx = event.clientX - (rect.left + radius);
        const dy = event.clientY - (rect.top + rect.height / 2);
        const hue = (Math.atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        pending = [hue, Math.min(1, Math.hypot(dx, dy) / radius)];
    };
    const send = async () => {
        frame = 0;
        if (sending || !pending || disposed || disabled()) return;
        sending = true;
        const next = pending;
        pending = null;
        try { await reference.invokeMethodAsync('SetWheelAsync', ...next); }
        catch { disposed = true; }
        finally {
            sending = false;
            if (pending && !disposed) schedule();
        }
    };
    const schedule = () => { if (!frame) frame = requestAnimationFrame(send); };
    const down = event => {
        if (disabled() || (event.pointerType === 'mouse' && event.button !== 0) || pointer !== null) return;
        event.preventDefault();
        surface.focus({ preventScroll: true });
        pointer = event.pointerId;
        surface.setPointerCapture(pointer);
        position(event);
        schedule();
    };
    const move = event => {
        if (event.pointerId !== pointer) return;
        event.preventDefault();
        position(event);
        schedule();
    };
    const end = event => {
        if (event.pointerId !== pointer) return;
        if (event.type === 'pointerup') position(event);
        if (surface.hasPointerCapture(pointer)) surface.releasePointerCapture(pointer);
        pointer = null;
        if (frame) cancelAnimationFrame(frame);
        frame = 0;
        void send();
    };
    const key = event => {
        if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) event.preventDefault();
    };
    surface.addEventListener('pointerdown', down);
    surface.addEventListener('pointermove', move);
    surface.addEventListener('pointerup', end);
    surface.addEventListener('pointercancel', end);
    surface.addEventListener('keydown', key);
    instances.set(surface, () => {
        disposed = true;
        if (frame) cancelAnimationFrame(frame);
        if (pointer !== null && surface.hasPointerCapture(pointer)) surface.releasePointerCapture(pointer);
        surface.removeEventListener('pointerdown', down);
        surface.removeEventListener('pointermove', move);
        surface.removeEventListener('pointerup', end);
        surface.removeEventListener('pointercancel', end);
        surface.removeEventListener('keydown', key);
    });
}

export function detach(surface) {
    instances.get(surface)?.();
    instances.delete(surface);
}
