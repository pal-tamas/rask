// A file input that is sending says so, and says how far it has got.
//
// rask-loading.ts marks the control a press or a submit is waiting on. A file `change` is neither: the reader
// chose files, and the dispatch that follows — the upload, then the page's handler — can take as long as the
// files are big. So the nearest element around the input that carries `data-rask-loading` (Flux UI marks its
// upload's dropzone) gets, from the moment the files are chosen until the handler is done:
//   data-loading                — the styling hook, at once: a spinner in place of the icon;
//   --rask-progress             — how much has gone, as a percentage: `12%` (a width, as Flux's bar reads it);
//   --rask-progress-as-string   — the same as a string, `'12%'`, for a `content:` that prints it.
// Both are whole percents and both are Flux's own pair (it writes `0%` and `'0%'` on its upload at rest).
//
// How much has GONE is the host's to say, and each says what its transport can see. The Server host posts
// the files and reports the request body's own progress events; the WASM host has nothing to send — .NET reads
// the file through OpenReadStream, a chunk at a time, in the reader's tab — so there it is how much of the
// files the handler has READ, and stays at 0 for a handler that never opens them.
//
// CALLED OFF. A button inside the holder that says `data-rask-upload-cancel` (Ui.FileUploadDropzone draws one beside
// its bar) stops the upload in flight from that holder's input (rask-upload-cancel.ts): each host says, as it
// begins, what stopping means for it — the Server host aborts the request still carrying the files, the WASM host
// cancels the handler reading them — and the input is emptied, so choosing the same file again is a new choice.
// Files that have all arrived and whose handler the Server host is already running are past calling off.
//
// No DOM access at import: both host bundles import this, and so does a script fixture.

import {disown, own} from "./rask-owned.js";

// Not a scope (`within`, rask-loading.ts): that marks what is around a press, and holds no upload.
const HOLDER = '[data-rask-loading]:not([data-rask-loading="within"])';

/** One file input's dispatch in flight. */
export interface Upload {
    /** `loaded` of `total` bytes have gone. */
    progress(loaded: number, total: number): void;

    /** The dispatch is over — sent, handled and rendered, or failed. Safe to call twice. */
    end(): void;

    /** What calling the upload off does on this host: added by the host as it begins. */
    stopping(stop: () => void): void;

    /** Whether the reader called it off. */
    readonly cancelled: boolean;
}

/** `loaded / total` as the two custom properties carry it: a whole percent, never over 100. */
export function progressOf(loaded: number, total: number): string {
    return Math.floor((total > 0 ? Math.max(0, Math.min(1, loaded / total)) : 0) * 100) + "%";
}

const open = new WeakMap<Element, Upload>();
const NOTHING: Upload = {
    progress() { /* nothing marked */ }, end() { /* nothing marked */ }, stopping() { /* nothing to call off */ },
    cancelled: false,
};

/** The upload in flight from each holder, and how to call it off. */
const byHolder = new WeakMap<Element, () => void>();

/** The upload in flight from `input`, if any — how a chunk read finds the element to report on. */
export function uploadOf(input: Element | null): Upload | undefined {
    return input ? open.get(input) : undefined;
}

/** Marks the element around `input` as uploading. Pair every call with the returned `end`. */
export function beginUpload(input: Element): Upload {
    const holder = input.closest<HTMLElement>(HOLDER);
    if (!holder || holder.getAttribute("data-rask-loading") === "off") {
        return NOTHING;
    }
    const earlier = open.get(input);
    if (earlier) {
        earlier.end(); // the reader chose again before the first files had gone
    }
    // A mark the render wrote is the render's; only one this added is taken off again.
    const marked = !holder.hasAttribute("data-loading");
    if (marked) {
        own(holder, "data-loading");
        holder.setAttribute("data-loading", "");
    }
    own(holder, "style");
    let done = false;
    const stops: (() => void)[] = [];
    const upload: Upload = {
        cancelled: false,
        stopping(stop) {
            stops.push(stop);
        },
        progress(loaded, total) {
            if (done) {
                return;
            }
            const percent = progressOf(loaded, total);
            holder.style.setProperty("--rask-progress", percent);
            holder.style.setProperty("--rask-progress-as-string", "'" + percent + "'");
        },
        end() {
            if (done) {
                return;
            }
            done = true;
            open.delete(input);
            if (byHolder.get(holder) === cancel) byHolder.delete(holder);
            holder.style.removeProperty("--rask-progress");
            holder.style.removeProperty("--rask-progress-as-string");
            disown(holder, "style");
            if (marked) {
                disown(holder, "data-loading");
                holder.removeAttribute("data-loading");
            }
        },
    };
    const cancel = function (): void {
        if (done) return;
        (upload as {cancelled: boolean}).cancelled = true;
        for (const stop of stops) stop();
        upload.end();
        (input as HTMLInputElement).value = "";
    };
    open.set(input, upload);
    byHolder.set(holder, cancel);
    upload.progress(0, 1);
    return upload;
}

/** Calls off the upload in flight from the input inside `holder`; false when none is. */
export function cancelUploadIn(holder: Element): boolean {
    const cancel = byHolder.get(holder);
    if (!cancel) return false;
    cancel();
    return true;
}

/** The element that marks the upload around `el`, if any. */
export function holderOf(el: Element): Element | null {
    return el.closest(HOLDER);
}
