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
// The page can render again while the reader is asked — a poll, a board told of every change, a sheet drawn anew —
// as often as it likes, and the pressed control is by then drawn from a newer page, its handler a new closure, its
// id perhaps another: an event read from the page the question was asked on is long past what the host remembers.
// So a yes presses the SAME button again, as a fresh press of the page as it is now: the element pressed while it is
// still on the page asking the same question, or the one drawn under its key path in its place. What stands there
// asking something else, or nothing, is another action, and nothing runs. A button that left the page with nothing
// in its place is looked for by the pressed handler's id, its event read from the page the question was asked on,
// so the host runs the handler the reader said yes to, or nothing (Component.StaleEvents.cs).
//
// The browser's own confirm() holds the page still while the host renders on, and what it rendered waits to be put
// on screen: a yes there is a press made again too, once the page has caught up, not the stale one held.

import {currentPage, readFrom} from "./rask-batch.js";
import {seam} from "./rask-owned.js";

const HANDLER = "data-rask-on-click";
const QUESTION = "data-rask-confirm";
const EXPECT = "data-rask-confirm-expect";
const KEY = "data-rask-key";

// The element a yes is pressing again, while it is: it was asked already, as the element it stands for.
let pressing: Element | null = null;

/**
 * Whether the action `el` is about to send may go: true when it asks nothing. `again` makes the action a second time,
 * for a yes given after this has returned — a press on the same button unless given: in Ui.Confirm, and for a press
 * in the browser's own dialog, which may have held the page while it was drawn anew.
 */
export function confirmed(el: Element | null, again?: () => void): boolean {
    if (!el || el === pressing) return true;
    const question = el.getAttribute(QUESTION);
    if (question === null) return true;
    const expect = el.getAttribute(EXPECT);
    const make = again || pressAgain(el);
    const inDialog = seam.confirm ? seam.confirm(el, question, expect, make) : null;
    if (inDialog !== null) return inDialog;
    const yes = expect === null ? window.confirm(question) : window.prompt(question) === expect;
    if (!yes || again) return yes;
    onceSettled(make);
    return false;
}

// How long the page must go without a new one put on screen to have caught up, and the longest a press waits for it.
const SETTLE_BEAT = 100;
const SETTLE_MOST = 1500;

// Runs `make` once the page has caught up after the browser's dialog let go. What the host sent while it was open
// arrives only now, a page per task, behind anything the answer queues (measured: a press a task later was read from
// the page before them all). So the press waits for a beat with no page put on screen — on a page that never stops,
// for the most it waits — and is read from the page as it is.
function onceSettled(make: () => void): void {
    const until = performance.now() + SETTLE_MOST;
    let seen = currentPage();
    const beat = function (): void {
        const now = currentPage();
        if (now === seen || performance.now() >= until) {
            make();
            return;
        }
        seen = now;
        setTimeout(beat, SETTLE_BEAT);
    };
    setTimeout(beat, SETTLE_BEAT);
}

// The press on `el` made again as the page stands at the answer: a fresh press of the same button, or — once `el` has
// left the page with nothing under its key path asking the same — of the element carrying its handler's id, read
// from the page it was asked on, which the host runs only when it can show its handler is the one pressed.
function pressAgain(el: Element): () => void {
    const asked = ask(el);
    const keys = keyPath(el);
    const id = el.getAttribute(HANDLER);
    const page = currentPage();
    return function (): void {
        const same = sameButton(el, keys, asked, id !== null);
        if (same) {
            press(same);
            return;
        }
        const now = !el.isConnected && id !== null ? carrying(el, id) : null;
        if (now) readFrom(page, function () { press(now); });
    };
}

function press(el: HTMLElement): void {
    pressing = el;
    try {
        el.click();
    } finally {
        pressing = null;
    }
}

// What `el` asks, whole: the question and the answer it expects.
function ask(el: Element): string {
    return el.getAttribute(QUESTION) + "\n" + el.getAttribute(EXPECT);
}

// The keys from the document down to `el`, its own last: null when `el` has no key of its own.
function keyPath(el: Element): string | null {
    if (el.getAttribute(KEY) === null) return null;
    const keys: string[] = [];
    for (let at: Element | null = el; at; at = at.parentElement) {
        const key = at.getAttribute(KEY);
        if (key !== null) keys.push(key);
    }
    return JSON.stringify(keys);
}

// The button the reader pressed, on the page as it is now: `el` itself while it is still on it, or the one drawn
// under the same key path in its place — while it is a control of the same kind that asks the same question.
function sameButton(el: Element, keys: string | null, asked: string, handled: boolean): HTMLElement | null {
    if (el.isConnected) return pressable(el, el, asked, handled);
    if (keys === null) return null;
    let found: HTMLElement | null = null;
    const all = el.ownerDocument.querySelectorAll<HTMLElement>("[" + KEY + "]");
    for (let i = 0; i < all.length; i++) {
        if (keyPath(all[i]) !== keys || !pressable(all[i], el, asked, handled)) continue;
        if (found) return null;
        found = all[i];
    }
    return found;
}

function pressable(now: Element, was: Element, asked: string, handled: boolean): HTMLElement | null {
    return now.tagName === was.tagName && (now.getAttribute(HANDLER) !== null) === handled && ask(now) === asked
        ? now as HTMLElement
        : null;
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
