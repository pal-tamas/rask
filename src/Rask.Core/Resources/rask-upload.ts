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
// No DOM access at import: both host bundles import this, and so does a Node fixture.

import {disown, own} from "./rask-owned.js";

const HOLDER = "[data-rask-loading]";

/** One file input's dispatch in flight. */
export interface Upload {
    /** `loaded` of `total` bytes have gone. */
    progress(loaded: number, total: number): void;

    /** The dispatch is over — sent, handled and rendered, or failed. Safe to call twice. */
    end(): void;
}

/** `loaded / total` as the two custom properties carry it: a whole percent, never over 100. */
export function progressOf(loaded: number, total: number): string {
    return Math.floor((total > 0 ? Math.max(0, Math.min(1, loaded / total)) : 0) * 100) + "%";
}

const open = new WeakMap<Element, Upload>();
const NOTHING: Upload = {progress() { /* nothing marked */ }, end() { /* nothing marked */ }};

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
    const upload: Upload = {
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
            holder.style.removeProperty("--rask-progress");
            holder.style.removeProperty("--rask-progress-as-string");
            disown(holder, "style");
            if (marked) {
                disown(holder, "data-loading");
                holder.removeAttribute("data-loading");
            }
        },
    };
    open.set(input, upload);
    upload.progress(0, 1);
    return upload;
}
