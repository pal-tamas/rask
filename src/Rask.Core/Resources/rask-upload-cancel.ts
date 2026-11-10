// The press that calls an upload off: a button that says `data-rask-upload-cancel`, inside the element that marks
// the upload (rask-upload.ts). The press is the runtime's alone — it opens no picker (the button sits inside the
// upload's <label>) and reaches no handler of the page's: the page hears the files it was never sent, or a handler
// that stopped where it was told to.

import {cancelUploadIn, holderOf} from "./rask-upload.js";
import {near, page} from "./rask-owned.js";

const CANCEL = "[data-rask-upload-cancel]";

/** Hears a press on `target`: true when it called an upload off. */
export function pressed(target: EventTarget | null): boolean {
    const button = near(target, CANCEL);
    const holder = button ? holderOf(button) : null;
    return !!holder && cancelUploadIn(holder);
}

if (page) {
    page.addEventListener("click", function (e) {
        if (!near(e.target, CANCEL)) return;
        // Whether or not anything was in flight: a cancel that found nothing still opens no picker.
        e.preventDefault();
        e.stopPropagation();
        pressed(e.target);
    }, true);
}
