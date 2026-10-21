// Asking before an action, the same on both hosts: Livewire's `wire:confirm`, written by `.Confirm("…")`.
//
// `data-rask-confirm` holds the question, shown in the browser's own confirm(). With `data-rask-confirm-expect`
// beside it (`.Confirm("…", expect: name)`, Livewire's `.prompt`) the reader types the answer into prompt(), and
// the action goes only on an exact match. Asked by the hosts' click and submit listeners before anything is sent,
// so a cancelled action sends nothing — not the event, and not the fields waiting to go ahead of it.
//
// With Ui.Confirm in the layout the question is asked in that dialog instead (rask-confirm-dialog.ts, through the
// seam). It answers later, so the action is refused now and a yes makes it again: the same press, or the same
// submit by the same button, which is then let through.

import {seam} from "./rask-owned.js";

/**
 * Whether the action `el` is about to send may go: true when it asks nothing, or the reader said yes. `again`
 * makes the action a second time, for a dialog that answers after this has returned: a press on `el` unless given.
 */
export function confirmed(el: Element | null, again?: () => void): boolean {
    if (!el) return true;
    const question = el.getAttribute("data-rask-confirm");
    if (question === null) return true;
    const expect = el.getAttribute("data-rask-confirm-expect");
    const inDialog = seam.confirm
        ? seam.confirm(el, question, expect, again || function () { (el as HTMLElement).click(); })
        : null;
    if (inDialog !== null) return inDialog;
    return expect === null ? window.confirm(question) : window.prompt(question) === expect;
}

/** Whether a submit may go: the form asks first, then the button that submitted it. */
export function submitConfirmed(form: Element, submitter: Element | null): boolean {
    const again = function (): void { resubmit(form as HTMLFormElement, submitter); };
    return confirmed(form, again) && confirmed(submitter, again);
}

// The same submit again, by the same button while it is still one that submits this form.
function resubmit(form: HTMLFormElement, submitter: Element | null): void {
    if (!form.isConnected) return;
    try {
        form.requestSubmit(submitter as HTMLElement | null);
    } catch {
        form.requestSubmit();
    }
}
