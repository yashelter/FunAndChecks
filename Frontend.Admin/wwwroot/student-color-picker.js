const instances = new WeakMap();

export function attach(surface, reference) {
    detach(surface);
    let pointer = null, frame = 0, pending = null, sending = false, disposed = false;
    const disabled = () => surface.dataset.disabled === 'true';
    const position = event => {
        const rect = surface.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        pending = [Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)),
            1 - Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height))];
    };
    const send = async () => {
        frame = 0;
        if (sending || !pending || disposed || disabled()) return;
        sending = true;
        const next = pending;
        pending = null;
        try { await reference.invokeMethodAsync('SetSpectrumAsync', ...next); }
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
        if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) event.preventDefault();
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
