// Light / dark theme. The saved choice is applied by a script in <head> before the page draws.
window.appTheme = {
    get() {
        const theme = document.documentElement.getAttribute('data-theme');
        if (theme === 'light' || theme === 'dark') return theme;
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    },
    toggle() {
        const theme = this.get() === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', theme);
        try { localStorage.setItem('app-theme', theme); } catch { }
        return theme;
    }
};

// Browser helpers for the database manager (/manager).
window.sqlManager = (() => {
    const editors = new Map();
    let guardUnload = false;

    // ---------- Query editor ----------
    function lineCount(text) {
        let count = 1;
        for (let i = 0; i < text.length; i++) if (text.charCodeAt(i) === 10) count++;
        return count;
    }

    function refreshGutter(textarea) {
        const editor = editors.get(textarea);
        if (!editor) return;
        const length = textarea.value.length;
        if (editor.length !== length) {
            editor.length = length;
            const count = lineCount(textarea.value);
            if (editor.count !== count) {
                editor.count = count;
                let numbers = '';
                for (let i = 1; i <= count; i++) numbers += i + '\n';
                editor.gutter.textContent = numbers;
            }
        }
        editor.gutter.scrollTop = textarea.scrollTop;
    }

    function replaceSelection(textarea, text, selectStart, selectEnd) {
        textarea.focus();
        // execCommand keeps the browser's undo history
        if (!document.execCommand('insertText', false, text)) {
            textarea.setRangeText(text, textarea.selectionStart, textarea.selectionEnd, 'end');
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
        }
        if (selectStart !== undefined) textarea.setSelectionRange(selectStart, selectEnd);
    }

    function selectLines(textarea) {
        const value = textarea.value;
        const start = value.lastIndexOf('\n', textarea.selectionStart - 1) + 1;
        let end = value.indexOf('\n', Math.max(textarea.selectionEnd - (textarea.selectionEnd > textarea.selectionStart ? 1 : 0), start));
        if (end < 0) end = value.length;
        textarea.setSelectionRange(start, end);
        return { start, lines: value.substring(start, end).split('\n') };
    }

    function indent(textarea, outdent) {
        const multiLine = textarea.value.substring(textarea.selectionStart, textarea.selectionEnd).includes('\n');
        if (!multiLine && !outdent) {
            replaceSelection(textarea, '    ');
            return;
        }
        const { start, lines } = selectLines(textarea);
        const changed = lines.map(l => outdent ? l.replace(/^( {1,4}|\t)/, '') : '    ' + l).join('\n');
        replaceSelection(textarea, changed, start, start + changed.length);
    }

    function toggleComment(textarea) {
        const { start, lines } = selectLines(textarea);
        const uncomment = lines.filter(l => l.trim()).every(l => /^\s*--/.test(l));
        const changed = lines.map(l => uncomment ? l.replace(/^(\s*)-- ?/, '$1') : (l.trim() ? '-- ' + l : l)).join('\n');
        replaceSelection(textarea, changed, start, start + changed.length);
    }

    function onKeyDown(e) {
        const textarea = e.currentTarget;
        const editor = editors.get(textarea);
        if (!editor) return;
        const ctrl = e.ctrlKey || e.metaKey;
        const key = e.key.toLowerCase();

        if (ctrl && e.key === 'F5') {
            e.preventDefault();
            editor.ref.invokeMethodAsync('OnShortcut', 'parse');
        } else if (e.key === 'F5' || (ctrl && key === 'e')) {
            e.preventDefault();
            editor.ref.invokeMethodAsync('OnShortcut', 'execute');
        } else if (e.altKey && (e.key === 'Pause' || e.key === 'Cancel')) {
            e.preventDefault();
            editor.ref.invokeMethodAsync('OnShortcut', 'cancel');
        } else if (ctrl && key === 's') {
            e.preventDefault();
            editor.ref.invokeMethodAsync('OnShortcut', 'save');
        } else if (ctrl && e.key === '/') {
            e.preventDefault();
            toggleComment(textarea);
        } else if (e.key === 'Tab' && !ctrl && !e.altKey) {
            e.preventDefault();
            indent(textarea, e.shiftKey);
        } else if (e.key === 'Enter' && !ctrl && !e.altKey && !e.shiftKey) {
            // Keep the indentation of the current line
            const value = textarea.value;
            const lineStart = value.lastIndexOf('\n', textarea.selectionStart - 1) + 1;
            const indentation = /^[ \t]*/.exec(value.substring(lineStart, textarea.selectionStart))[0];
            if (indentation) {
                e.preventDefault();
                replaceSelection(textarea, '\n' + indentation);
            }
        }
    }

    function registerEditor(textarea, gutter, ref) {
        if (!textarea || editors.has(textarea)) return;
        const editor = { gutter, ref, count: 0, length: -1 };
        editor.onScroll = () => { gutter.scrollTop = textarea.scrollTop; };
        editor.onInput = () => refreshGutter(textarea);
        editors.set(textarea, editor);
        textarea.addEventListener('keydown', onKeyDown);
        textarea.addEventListener('scroll', editor.onScroll);
        textarea.addEventListener('input', editor.onInput);
        refreshGutter(textarea);
    }

    function unregisterEditor(textarea) {
        const editor = editors.get(textarea);
        if (!editor) return;
        textarea.removeEventListener('keydown', onKeyDown);
        textarea.removeEventListener('scroll', editor.onScroll);
        textarea.removeEventListener('input', editor.onInput);
        editors.delete(textarea);
    }

    function getEditorState(textarea) {
        return { text: textarea.value, start: textarea.selectionStart, end: textarea.selectionEnd };
    }

    function focusLine(textarea, line) {
        const lines = textarea.value.split('\n');
        line = Math.max(1, Math.min(line, lines.length));
        let start = 0;
        for (let i = 0; i < line - 1; i++) start += lines[i].length + 1;
        textarea.focus();
        textarea.setSelectionRange(start, start + lines[line - 1].length);
        const lineHeight = parseFloat(getComputedStyle(textarea).lineHeight) || 20;
        textarea.scrollTop = Math.max(0, (line - 1) * lineHeight - textarea.clientHeight / 2);
        refreshGutter(textarea);
    }

    function focus(element) {
        if (typeof element === 'string') element = document.querySelector(element);
        if (element) element.focus();
    }

    // F5 anywhere on the manager page runs the visible query instead of reloading (and losing) the page
    document.addEventListener('keydown', e => {
        if (e.key !== 'F5' || e.ctrlKey || e.defaultPrevented || !document.querySelector('.mgr')) return;
        for (const [textarea, editor] of editors) {
            if (textarea.offsetParent !== null && textarea.closest('.tab-pane:not([hidden])')) {
                e.preventDefault();
                editor.ref.invokeMethodAsync('OnShortcut', 'execute');
                return;
            }
        }
    });

    window.addEventListener('beforeunload', e => {
        if (guardUnload && document.querySelector('.mgr')) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    function setUnloadGuard(value) {
        guardUnload = !!value;
    }

    // ---------- Splitters ----------
    function initSplitter(handle, target, axis, storageKey, min, max) {
        if (!handle || !target || handle.dataset.ready) return;
        handle.dataset.ready = '1';
        const property = axis === 'y' ? 'height' : 'width';
        try {
            const saved = parseFloat(localStorage.getItem(storageKey));
            if (saved >= min && saved <= max) target.style[property] = saved + 'px';
        } catch { }

        handle.addEventListener('pointerdown', down => {
            down.preventDefault();
            handle.setPointerCapture(down.pointerId);
            const rtl = getComputedStyle(handle).direction === 'rtl';
            const startSize = target.getBoundingClientRect()[property];
            const startPos = axis === 'y' ? down.clientY : down.clientX;
            document.body.classList.add('resizing-' + axis);

            const move = e => {
                let delta = (axis === 'y' ? e.clientY : e.clientX) - startPos;
                if (axis === 'x' && rtl) delta = -delta;
                const size = Math.min(max, Math.max(min, startSize + delta));
                target.style[property] = size + 'px';
            };
            const up = () => {
                handle.removeEventListener('pointermove', move);
                handle.removeEventListener('pointerup', up);
                handle.removeEventListener('pointercancel', up);
                document.body.classList.remove('resizing-' + axis);
                try { localStorage.setItem(storageKey, parseFloat(target.style[property])); } catch { }
            };
            handle.addEventListener('pointermove', move);
            handle.addEventListener('pointerup', up);
            handle.addEventListener('pointercancel', up);
        });
    }

    // ---------- Misc ----------
    function clampToViewport(element) {
        if (!element) return;
        const rect = element.getBoundingClientRect();
        if (rect.right > window.innerWidth - 4) element.style.left = Math.max(4, window.innerWidth - rect.width - 4) + 'px';
        if (rect.bottom > window.innerHeight - 4) element.style.top = Math.max(4, window.innerHeight - rect.height - 4) + 'px';
        if (rect.left < 4) element.style.left = '4px';
    }

    function download(fileName, content, mime) {
        const blob = new Blob([content], { type: mime || 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    async function copyText(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // navigator.clipboard needs a secure context (https or localhost)
            const area = document.createElement('textarea');
            area.value = text;
            area.style.position = 'fixed';
            area.style.opacity = '0';
            document.body.appendChild(area);
            area.select();
            const ok = document.execCommand('copy');
            area.remove();
            return ok;
        }
    }

    function clickElement(element) {
        if (element) element.click();
    }

    return {
        registerEditor, unregisterEditor, refreshGutter, getEditorState, focusLine, focus,
        setUnloadGuard, initSplitter, clampToViewport, download, copyText, clickElement
    };
})();
