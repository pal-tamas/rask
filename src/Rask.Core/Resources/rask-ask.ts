// A question the kit's own dialog asks in place of the browser's: Ui.ConfirmLeave (rask-leave.ts) and Ui.Confirm
// (rask-confirm-dialog.ts). Each is a Ui.Modal the layout placed once, rendered closed, whose parts say
// `<part>=message` (where the question is written) and `<part>=go` (the button that says yes).
//
// A dialog in the page answers later than `confirm` does, so the caller refuses what was asked for now and hands
// over `yes`, the same thing again. It runs once the dialog has CLOSED by the yes button: focus is back where it
// was by then, and whatever `yes` asks in turn finds the dialog free to ask it. Every other way the dialog closes
// (the other button, the X, Escape, a press outside) is no, and needs nothing but `done`.

import {listen, near, page} from "./rask-owned.js";

/**
 * Asks `message` in the dialog; null when the layout placed none, and the browser's own dialog is to ask. `done`
 * runs as it closes, either way, and `ready` on the dialog just before it opens.
 */
export type Ask = (message: string, yes: () => void, done?: () => void, ready?: (dialog: HTMLDialogElement) => void)
    => HTMLDialogElement | null;

/** The kit's dialog whose parts carry the attribute `part`. */
export function dialogAsking(part: string): Ask {
    let asking: HTMLDialogElement | null = null;
    let said = false;
    let yes: (() => void) | null = null;
    let done: (() => void) | null = null;

    listen("click", function (e) {
        if (asking && near(e.target, "[" + part + "=go]")) said = true;
    });
    // `close` does not bubble: heard on the way down.
    listen("close", function (e) {
        if (e.target !== asking) return;
        const then = said ? yes : null;
        const after = done;
        asking = yes = done = null;
        said = false;
        if (after) after();
        if (then) then();
    }, true);

    return function (message, then, after, ready) {
        const text = page ? page.querySelector("[" + part + "=message]") : null;
        const dialog = text ? text.closest("dialog") : null;
        if (!text || !dialog) return null;
        text.textContent = message;
        asking = dialog;
        yes = then;
        done = after || null;
        said = false;
        if (ready) ready(dialog);
        // The platform's own call, which is what the dialog's invoker buttons make: rask-overlay.ts marks it open
        // and dismisses it as it does any Ui.Modal, and the browser moves focus in and hands it back.
        if (!dialog.open) dialog.showModal();
        return dialog;
    };
}
