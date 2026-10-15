// Keys that send a form from the text typed into it (data-rask-submit-keys).
//
// A message box submits its form from the keyboard, and the key has to be taken in the keydown itself: a round
// trip later the newline is already in the text. So the box names the key on an element around what is typed:
//
//   data-rask-submit-keys="Mod+Enter"   Ctrl+Enter or Cmd+Enter submits; Enter alone is a new line
//   data-rask-submit-keys="Enter"       Enter submits, with or without Ctrl, Cmd or Alt; Shift+Enter is a new line
//
// pressed in a <textarea> or an editable area inside it. The key is cancelled and kept from the field (a rich
// editor would otherwise break the line), and the field's form is submitted as its submit button would submit
// it — validation first, the `submit` event the page listens to after. Outside a form the key is left alone.
// (Flux UI's composer, measured: in its default mode Cmd+Enter and Ctrl+Enter submit and Alt+Enter does
// nothing; with submit="enter" any Enter but Shift+Enter submits, an empty box too; neither clears the box.)

import {listen, near, page} from "./rask-owned.js";

const KEYS = "data-rask-submit-keys";

/** Whether `keys` (`Enter` or `Mod+Enter`) says this keydown submits. Exported for the fixture. */
export function submits(keys: string, e: Pick<KeyboardEvent, "key" | "shiftKey" | "ctrlKey" | "metaKey">): boolean {
    if (e.key !== "Enter" || e.shiftKey) {
        return false;
    }
    return keys === "Enter" || (keys === "Mod+Enter" && (e.ctrlKey || e.metaKey));
}

function typedInto(t: Element): boolean {
    return t instanceof HTMLTextAreaElement || (t instanceof HTMLElement && t.isContentEditable);
}

if (page) {
    listen("keydown", function (e) {
        const t = e.target;
        const box = near(t, "[" + KEYS + "]");
        if (!box || !(t instanceof Element) || !typedInto(t) || !submits(box.getAttribute(KEYS) || "", e)) {
            return;
        }
        const form = box.closest("form");
        if (!form) {
            return;
        }
        e.preventDefault();
        e.stopPropagation();
        form.requestSubmit();
    }, true);
}
