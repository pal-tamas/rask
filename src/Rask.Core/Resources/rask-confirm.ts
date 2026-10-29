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
//
// The page can render again while the reader is asked — a poll, a toast arriving, a sheet drawn anew — and the
// pressed control may by then be another element, or gone. A press is made again on the element that carries the
// pressed handler's id at the answer, its event saying it was read from the page the question was asked on, so the
// host runs the handler the reader said yes to, wherever it is now, or nothing (Component.StaleEvents.cs) — never
// another one that took its place.

import {currentPage, readFrom} from "./rask-batch.js";
import {seam} from "./rask-owned.js";

const HANDLER = "data-rask-on-click";

// The element a yes is pressing again, while it is: it was asked already, as the element it stands for.
let pressing: Element | null = null;

/**
 * Whether the action `el` is about to send may go: true when it asks nothing, or the reader said yes. `again`
 * makes the action a second time, for a dialog that answers after this has returned: a press on `el` unless given.
 */
export function confirmed(el: Element | null, again?: () => void): boolean {
    if (!el || el === pressing) return true;
    const question = el.getAttribute("data-rask-confirm");
    if (question === null) return true;
    const expect = el.getAttribute("data-rask-confirm-expect");
    const inDialog = seam.confirm
        ? seam.confirm(el, question, expect, again || pressAgain(el))
        : null;
    if (inDialog !== null) return inDialog;
    return expect === null ? window.confirm(question) : window.prompt(question) === expect;
}

// The press on `el` made again as the page stands at the answer: on the element carrying its handler's id, read
// from the page it was asked on. Nothing when no element carries that id any more.
function pressAgain(el: Element): () => void {
    const id = el.getAttribute(HANDLER);
    const asked = currentPage();
    return function (): void {
        if (id === null) {
            if (el.isConnected) (el as HTMLElement).click();
            return;
        }
        const now = carrying(el, id);
        if (!now) return;
        pressing = now;
        try {
            readFrom(asked, function () { now.click(); });
        } finally {
            pressing = null;
        }
    };
}

// The element carrying handler `id` now: `el` itself while it still does, else the one that took its place.
function carrying(el: Element, id: string): HTMLElement | null {
    if (el.isConnected && el.getAttribute(HANDLER) === id) return el as HTMLElement;
    const all = el.ownerDocument.querySelectorAll<HTMLElement>("[" + HANDLER + "]");
    for (let i = 0; i < all.length; i++) {
        if (all[i].getAttribute(HANDLER) === id) return all[i];
    }
    return null;
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
