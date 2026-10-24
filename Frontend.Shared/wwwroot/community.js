// Помощники форума: вставка файлов из буфера и перетаскивание прямо в InputFile,
// чтобы Blazor получил их обычным событием change.
const bindings = new WeakMap();

function forward(input, files) {
    if (!files || files.length === 0) return false;
    const transfer = new DataTransfer();
    for (const file of files) transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
}

export function bindComposer(zone, input) {
    if (!zone || !input || bindings.has(zone)) return;
    const onPaste = event => {
        const files = [...(event.clipboardData?.files ?? [])];
        if (files.length > 0 && forward(input, files)) event.preventDefault();
    };
    const onDragOver = event => {
        if ([...(event.dataTransfer?.types ?? [])].includes('Files')) {
            event.preventDefault();
            zone.classList.add('dragging');
        }
    };
    const onDragLeave = () => zone.classList.remove('dragging');
    const onDrop = event => {
        zone.classList.remove('dragging');
        if (forward(input, [...(event.dataTransfer?.files ?? [])])) event.preventDefault();
    };
    zone.addEventListener('paste', onPaste);
    zone.addEventListener('dragover', onDragOver);
    zone.addEventListener('dragleave', onDragLeave);
    zone.addEventListener('drop', onDrop);
    bindings.set(zone, () => {
        zone.removeEventListener('paste', onPaste);
        zone.removeEventListener('dragover', onDragOver);
        zone.removeEventListener('dragleave', onDragLeave);
        zone.removeEventListener('drop', onDrop);
    });
}

/**
 * Горячие клавиши поля сообщения: Ctrl/Cmd+Enter — отправить, Ctrl/Cmd+B/I — жирный/курсив,
 * Esc — отменить правку, «@» в начале слова — выбрать, кого упомянуть.
 */
export function bindShortcuts(textarea, dotnet) {
    if (!textarea || textarea.dataset.fcShortcuts) return;
    textarea.dataset.fcShortcuts = '1';
    textarea.addEventListener('keydown', event => {
        const mod = event.ctrlKey || event.metaKey;
        if (mod && event.key === 'Enter') { event.preventDefault(); dotnet.invokeMethodAsync('SubmitFromKeyboard'); return; }
        if (mod && (event.key === 'b' || event.key === 'B')) { event.preventDefault(); wrapAndNotify(textarea, '**', '**', dotnet); return; }
        if (mod && (event.key === 'i' || event.key === 'I')) { event.preventDefault(); wrapAndNotify(textarea, '_', '_', dotnet); return; }
        if (event.key === 'Escape') { dotnet.invokeMethodAsync('CancelFromKeyboard'); return; }
        if (event.key === '@') {
            const before = textarea.value.slice(0, textarea.selectionStart ?? 0);
            if (before.length === 0 || /\s$/.test(before)) dotnet.invokeMethodAsync('OpenMentionsFromKeyboard');
        }
    });
}

function wrapAndNotify(textarea, before, after, dotnet) {
    const value = wrapSelection(textarea, before, after);
    if (value !== null) dotnet.invokeMethodAsync('BodyChangedFromKeyboard', value);
}

/** Вставляет упоминание; если перед курсором набран «@», он заменяется. */
export function insertMention(textarea, text) {
    if (!textarea) return null;
    let start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    if (start > 0 && textarea.value[start - 1] === '@') start -= 1;
    const value = textarea.value.slice(0, start) + text + textarea.value.slice(end);
    textarea.value = value;
    const caret = start + text.length;
    textarea.focus();
    textarea.setSelectionRange(caret, caret);
    return value;
}

/** Черновики сообщений: не теряются при случайном уходе со страницы. */
export function loadDraft(key) {
    try { return localStorage.getItem(key); } catch { return null; }
}

export function saveDraft(key, value) {
    try {
        if (value && value.trim().length > 0) localStorage.setItem(key, value);
        else localStorage.removeItem(key);
    } catch { /* приватный режим и т.п. */ }
}

export function focusElement(element) { element?.focus(); }

export function unbindComposer(zone) {
    const release = zone && bindings.get(zone);
    if (release) { release(); bindings.delete(zone); }
}

export function openFilePicker(input) { input?.click(); }

/** Вставляет текст в позицию курсора textarea и возвращает новое значение. */
export function insertAtCursor(textarea, text) {
    if (!textarea) return null;
    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    const value = textarea.value.slice(0, start) + text + textarea.value.slice(end);
    textarea.value = value;
    const caret = start + text.length;
    textarea.focus();
    textarea.setSelectionRange(caret, caret);
    return value;
}

/** Оборачивает выделение маркерами Markdown (**, `, …). */
export function wrapSelection(textarea, before, after) {
    if (!textarea) return null;
    const start = textarea.selectionStart ?? 0;
    const end = textarea.selectionEnd ?? start;
    const selected = textarea.value.slice(start, end);
    const value = textarea.value.slice(0, start) + before + selected + after + textarea.value.slice(end);
    textarea.value = value;
    textarea.focus();
    textarea.setSelectionRange(start + before.length, start + before.length + selected.length);
    return value;
}

export function scrollToElement(id) {
    const element = document.getElementById(id);
    if (!element) return false;
    element.scrollIntoView({ behavior: 'smooth', block: 'center' });
    element.classList.add('fc-highlight');
    setTimeout(() => element.classList.remove('fc-highlight'), 2200);
    return true;
}

export async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
}
