// Scoped TypeScript for CodeSample, exposed as Rask.CodeSample.copy. The C# side passes the raw
// (un-highlighted) source of the active tab plus an ElementRef to the copy button; the runtime
// reviver resolves the ref to the live element before this runs.
export async function copy(text: string, btn: HTMLElement | null): Promise<void> {
    try {
        await navigator.clipboard.writeText(text);
    } catch {
        // Clipboard can reject (permissions, insecure context). Fall back to a hidden
        // textarea + execCommand so the button still works on http/older browsers.
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        try {
            document.execCommand('copy');
        } catch {
            // Nothing else to try — swallow so the "Copied!" flash still fires.
        }
        ta.remove();
    }

    if (!btn) {
        return;
    }

    // Flash "Copied!" for a moment, then restore — transient UI state lives on the client
    // so it never costs a server round-trip or a re-render.
    //
    // The label is typed as the base Element rather than HTMLElement: querySelector returns
    // Element | null, and textContent is what this touches, which Element already has.
    const label: Element | null = btn.querySelector('.sample-copy-text');
    const previous = label ? label.textContent : null;
    btn.classList.add('copied');
    if (label) {
        label.textContent = 'Copied!';
    }

    setTimeout(() => {
        btn.classList.remove('copied');
        if (label && previous !== null) {
            label.textContent = previous;
        }
    }, 1500);
}
