const urls = new Set(), observers = new WeakMap();
export async function objectUrl(stream, type) { const bytes = await stream.arrayBuffer(); const url = URL.createObjectURL(new Blob([bytes], { type })); urls.add(url); return url; }
export function release(url) { if (url) { URL.revokeObjectURL(url); urls.delete(url); } }
export function clear() { for (const url of urls) URL.revokeObjectURL(url); urls.clear(); }
window.profileMedia = { clear };
export function observe(element, poster, animated) {
    unobserve(element);
    const observer = new IntersectionObserver(entries => {
        const child = element.querySelector('video,img'); if (!child) return;
        const visible = entries[0].isIntersecting && !document.hidden;
        if (child.tagName === 'VIDEO') { child.muted = true; if (visible) child.play().catch(() => {}); else child.pause(); }
        else if (poster) child.src = visible ? animated : poster;
    }, { rootMargin: '50px' });
    observer.observe(element); observers.set(element, observer);
}
export function unobserve(element) { const previous = observers.get(element); if (previous?.disconnect) previous.disconnect(); else if (previous) { previous.load.disconnect(); previous.animation.disconnect(); document.removeEventListener('visibilitychange', previous.visibility); } observers.delete(element); }
export function observeMedia(element, reference) {
    unobserve(element);
    const load = new IntersectionObserver(entries => {
        reference.invokeMethodAsync('VisibilityChangedAsync', entries[0].isIntersecting).catch(() => {});
    }, { rootMargin: '100px' });
    const animation = new IntersectionObserver(entries => { element._profileVisible = entries[0].isIntersecting; refreshMedia(element, element._profilePoster, element._profileAnimated); });
    const visibility = () => refreshMedia(element, element._profilePoster, element._profileAnimated);
    load.observe(element); animation.observe(element); document.addEventListener('visibilitychange', visibility);
    observers.set(element, { load, animation, visibility });
}
export function refreshMedia(element, poster, animated) {
    element._profilePoster = poster; element._profileAnimated = animated;
    const child = element.querySelector('video,img'); if (!child) return;
    const visible = element._profileVisible && !document.hidden;
    if (child.tagName === 'VIDEO') { child.muted = true; if (visible) child.play().catch(() => {}); else child.pause(); }
    else if (poster && animated) child.src = visible ? animated : poster;
}
export function focusDialog(element) { element.querySelector('input:not([disabled]),textarea:not([disabled]),button:not([disabled]),[tabindex]')?.focus(); }
export function seek(element, seconds) { const video = element.querySelector('video'); if (video) { video.pause(); video.currentTime = seconds; } }
export function loopSegment(element, start, end, still) {
    const video = element.querySelector('video'); if (!video) return;
    video.muted = true;
    const frame = element.querySelector('.fc-profile-crop-frame'); if (frame) frame.style.visibility = still ? 'visible' : 'hidden';
    video.ontimeupdate = () => { if (!still && video.currentTime >= end) video.currentTime = start; };
    if (still) video.pause(); else { if (video.currentTime < start || video.currentTime >= end) video.currentTime = start; video.play().catch(() => {}); }
}
const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
function applyCrop(element) {
    const state = element._profileCrop; if (!state) return;
    const scale = state.zoom / Math.min(state.width, state.height);
    const width = state.width * scale * 100, height = state.height * scale * 100;
    element.style.setProperty('--crop-width', width + '%'); element.style.setProperty('--crop-height', height + '%');
    element.style.setProperty('--crop-left', -(width - 100) * state.x + '%'); element.style.setProperty('--crop-top', -(height - 100) * state.y + '%');
}
export function configureCrop(element, width, height, zoom, x, y, busy) {
    element._profileCrop = { width, height, zoom, x, y, busy }; applyCrop(element);
}
export function readCrop(element) { const state = element._profileCrop; return { x: state?.x ?? .5, y: state?.y ?? .5, zoom: state?.zoom ?? 1 }; }
export function attachCrop(element, reference) {
    if (element._profileCropAttached) return;
    element._profileCropAttached = true;
    const points = new Map(); let gesture;
    const distance = () => { const pair = [...points.values()]; return Math.hypot(pair[0].x - pair[1].x, pair[0].y - pair[1].y); };
    const beginGesture = () => {
        const state = element._profileCrop; if (!state || !points.size) return;
        const point = [...points.values()][0];
        gesture = { x: point.x, y: point.y, focusX: state.x, focusY: state.y, zoom: state.zoom, distance: points.size > 1 ? Math.max(.1, distance()) : 0 };
    };
    const commit = () => { const state = element._profileCrop; if (state) reference.invokeMethodAsync('CropChangedAsync', state.x, state.y, state.zoom).catch(() => {}); };
    element.addEventListener('pointerdown', e => {
        if (!element._profileCrop || element._profileCrop.busy || (e.pointerType === 'mouse' && e.button !== 0)) return;
        e.preventDefault(); element.focus({ preventScroll: true });
        points.set(e.pointerId, { x: e.clientX, y: e.clientY }); element.setPointerCapture(e.pointerId); beginGesture();
        element.classList.add('dragging');
    });
    element.addEventListener('pointermove', e => {
        const state = element._profileCrop; if (!state || state.busy || !points.has(e.pointerId) || !gesture) return;
        points.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (points.size > 1) state.zoom = clamp(gesture.zoom * distance() / gesture.distance, 1, 4);
        else {
            const scale = element.clientWidth * state.zoom / Math.min(state.width, state.height);
            const overflowX = state.width * scale - element.clientWidth, overflowY = state.height * scale - element.clientHeight;
            state.x = overflowX > .1 ? clamp(gesture.focusX - (e.clientX - gesture.x) / overflowX, 0, 1) : .5;
            state.y = overflowY > .1 ? clamp(gesture.focusY - (e.clientY - gesture.y) / overflowY, 0, 1) : .5;
        }
        applyCrop(element);
    });
    const finish = e => {
        if (!points.has(e.pointerId)) return; points.delete(e.pointerId);
        if (element.hasPointerCapture(e.pointerId)) element.releasePointerCapture(e.pointerId);
        if (points.size) beginGesture(); else { gesture = null; element.classList.remove('dragging'); commit(); }
    };
    element.addEventListener('pointerup', finish); element.addEventListener('pointercancel', finish);
    element.addEventListener('wheel', e => {
        const state = element._profileCrop; if (!state || state.busy) return;
        e.preventDefault(); state.zoom = clamp(state.zoom + (e.deltaY < 0 ? .1 : -.1), 1, 4); applyCrop(element); commit();
    }, { passive: false });
    element.addEventListener('keydown', e => {
        const state = element._profileCrop; if (!state || state.busy) return;
        const scale = element.clientWidth * state.zoom / Math.min(state.width, state.height), step = e.shiftKey ? 20 : 8;
        const horizontal = Math.max(1, state.width * scale - element.clientWidth), vertical = Math.max(1, state.height * scale - element.clientHeight);
        if (e.key === 'ArrowLeft') state.x = clamp(state.x + step / horizontal, 0, 1);
        else if (e.key === 'ArrowRight') state.x = clamp(state.x - step / horizontal, 0, 1);
        else if (e.key === 'ArrowUp') state.y = clamp(state.y + step / vertical, 0, 1);
        else if (e.key === 'ArrowDown') state.y = clamp(state.y - step / vertical, 0, 1);
        else if (e.key === '+' || e.key === '=') state.zoom = clamp(state.zoom + .1, 1, 4);
        else if (e.key === '-') state.zoom = clamp(state.zoom - .1, 1, 4);
        else return;
        e.preventDefault(); applyCrop(element); commit();
    });
}
function timeLabel(value) { const total = Math.max(0, value); return String(Math.floor(total / 60)).padStart(2, '0') + ':' + (total % 60).toFixed(1).padStart(4, '0'); }
function applyTimeline(element) {
    const state = element._profileTimeline; if (!state) return;
    const percent = value => 100 * value / Math.max(.001, state.duration);
    for (const handle of element.querySelectorAll('[data-boundary]')) {
        const boundary = handle.dataset.boundary, value = state[boundary];
        handle.style.left = percent(value) + '%'; handle.setAttribute('aria-valuenow', value.toFixed(2)); handle.setAttribute('aria-valuetext', timeLabel(value));
        if (boundary === 'end') { handle.setAttribute('aria-valuemin', String(state.start + Math.min(.1, state.duration))); handle.setAttribute('aria-valuemax', String(Math.min(state.duration, state.start + 10))); }
    }
    const selection = element.querySelector('.fc-profile-timeline-selection'); if (selection) { selection.style.left = percent(state.start) + '%'; selection.style.width = percent(state.end - state.start) + '%'; }
    const start = element.querySelector('[data-time="start"]'), end = element.querySelector('[data-time="end"]');
    if (start) start.textContent = timeLabel(state.still ? state.frame : state.start); if (end) end.textContent = state.still ? '' : timeLabel(state.end);
}
export function configureTimeline(element, duration, start, end, frame, still, busy) {
    element._profileTimeline = { duration, start, end, frame, still, busy }; applyTimeline(element);
}
export function readTimeline(element) { const state = element._profileTimeline; return { start: state?.start ?? 0, end: state?.end ?? 0, frame: state?.frame ?? 0 }; }
export function attachTimeline(element, preview, reference) {
    if (element._profileTimelineAttached) return;
    element._profileTimelineAttached = true; let boundary = null, pointer;
    const commit = () => { const state = element._profileTimeline; if (state) reference.invokeMethodAsync('TimelineChangedAsync', state.start, state.end, state.frame).catch(() => {}); };
    const update = value => {
        const state = element._profileTimeline; if (!state) return;
        const span = Math.min(.1, state.duration);
        if (boundary === 'frame') state.frame = clamp(value, 0, Math.max(0, state.duration - .01));
        else if (boundary === 'start') { state.start = clamp(value, 0, Math.max(0, state.duration - span)); state.end = clamp(state.end, state.start + span, Math.min(state.duration, state.start + 10)); }
        else if (boundary === 'end') state.end = clamp(value, state.start + span, Math.min(state.duration, state.start + 10));
        applyTimeline(element);
        const video = preview.querySelector('video');
        if (state.still && video) { preview.querySelector('.fc-profile-crop-frame')?.style.setProperty('visibility', 'hidden'); video.pause(); video.currentTime = state.frame; }
        else loopSegment(preview, state.start, state.end, false);
    };
    const fromPointer = e => { const track = element.querySelector('.fc-profile-timeline-track').getBoundingClientRect(); return clamp((e.clientX - track.left) / Math.max(1, track.width), 0, 1) * element._profileTimeline.duration; };
    element.addEventListener('pointerdown', e => {
        const state = element._profileTimeline; if (!state || state.busy || (e.pointerType === 'mouse' && e.button !== 0) || !e.target.closest('.fc-profile-timeline-track')) return;
        const value = fromPointer(e); boundary = e.target.closest('[data-boundary]')?.dataset.boundary || (state.still ? 'frame' : Math.abs(value - state.start) < Math.abs(value - state.end) ? 'start' : 'end');
        e.preventDefault(); pointer = e.pointerId; element.setPointerCapture(pointer); element.querySelector('[data-boundary="' + boundary + '"]')?.focus({ preventScroll: true }); update(value);
    });
    element.addEventListener('pointermove', e => { if (e.pointerId === pointer && boundary) update(fromPointer(e)); });
    const finish = e => { if (e.pointerId !== pointer || !boundary) return; if (element.hasPointerCapture(pointer)) element.releasePointerCapture(pointer); boundary = null; pointer = null; commit(); };
    element.addEventListener('pointerup', finish); element.addEventListener('pointercancel', finish);
    element.addEventListener('keydown', e => {
        const handle = e.target.closest('[data-boundary]'), state = element._profileTimeline; if (!handle || !state || state.busy) return;
        boundary = handle.dataset.boundary; const step = e.shiftKey ? .5 : .05; let value = state[boundary];
        if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') value -= step;
        else if (e.key === 'ArrowRight' || e.key === 'ArrowUp') value += step;
        else if (e.key === 'Home') value = boundary === 'end' ? state.start + Math.min(.1, state.duration) : 0;
        else if (e.key === 'End') value = boundary === 'end' ? Math.min(state.duration, state.start + 10) : state.duration;
        else { boundary = null; return; }
        e.preventDefault(); update(value); boundary = null; commit();
    });
}
const dialogs = [];
let previousBodyOverflow;
function updateDialogLayers() {
    for (let index = 0; index < dialogs.length; index++) {
        const backdrop = dialogs[index].closest('.fc-profile-modal-backdrop');
        if (!backdrop) continue;
        const top = index === dialogs.length - 1;
        backdrop.inert = !top;
        backdrop.style.zIndex = String(1500 + index * 10);
        if (top) backdrop.removeAttribute('aria-hidden'); else backdrop.setAttribute('aria-hidden', 'true');
    }
}
export function isTopDialog(element) { return dialogs.at(-1) === element; }
export function trapDialog(element) {
    if (dialogs.includes(element)) return;
    if (!dialogs.length) { previousBodyOverflow = document.body.style.overflow; document.body.style.overflow = 'hidden'; }
    const previous = document.activeElement;
    dialogs.push(element); updateDialogLayers();
    element._profileKey = e => {
        if (!isTopDialog(element)) { if (e.key === 'Escape' || e.key === 'Tab') e.stopImmediatePropagation(); return; }
        if (e.key !== 'Tab') return;
        const nodes = [...element.querySelectorAll('button:not([disabled]),input:not([disabled]),textarea:not([disabled]),select:not([disabled]),a[href]')].filter(x => x.offsetParent !== null);
        if (!nodes.length) return; const first = nodes[0], last = nodes.at(-1);
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    };
    element.addEventListener('keydown', element._profileKey); element._previousFocus = previous; focusDialog(element);
}
export function releaseDialog(element) {
    if (!element) return;
    const wasTop = isTopDialog(element);
    element.removeEventListener('keydown', element._profileKey);
    const index = dialogs.indexOf(element); if (index >= 0) dialogs.splice(index, 1);
    const backdrop = element.closest('.fc-profile-modal-backdrop'); if (backdrop) { backdrop.inert = false; backdrop.removeAttribute('aria-hidden'); }
    updateDialogLayers();
    if (!dialogs.length) document.body.style.overflow = previousBodyOverflow || '';
    if (!wasTop) return;
    const previous = element._previousFocus;
    if (previous?.isConnected && !previous.closest('[inert]')) previous.focus();
    else if (dialogs.length) focusDialog(dialogs.at(-1));
}
