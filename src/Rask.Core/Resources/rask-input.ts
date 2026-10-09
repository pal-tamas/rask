// rAF-coalesced input & scroll dispatch, shared by every client runtime.
//
// Imported by the Server runtime (rask.ts) and the WASM runtime (rask.wasm.ts) so the two clients can
// never drift. Its dependencies used to be three symbols "every host defines in the surrounding
// scope"; they are now imports, which is the difference between a convention and a contract.
//
// The ordering constraint the old splice carried in a comment — "MUST be spliced BEFORE
// rask-events.js, whose keyboard handler calls flushInputsNow()" — is gone. rask-events imports
// flushInputsNow from here, so the bundler orders them, and getting it wrong is a resolution error
// rather than a runtime one.

import { inRoot, send } from "./rask-host.js";
import {
    raskChangeFrameValue,
    raskChangeFrameValues,
    raskNoteDirtyField,
    raskNotePendingFormState,
} from "./rask-morph.js";
import { raskTargetState } from "./rask-dom-payload.js";

/** Any element this module reads a `value` off. */
type ValueElement = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

// Input events fire per keystroke — on fast typing that's 5–10 messages over the
// transport per second per input. Coalesce per-element with rAF: the same element typed into
// multiple times within one frame produces a single outgoing message carrying the latest value
// at flush time. The element itself is the de-duping key — multiple inputs in the same frame
// each get one message. Whatever a host sends next goes AFTER them (sendTypedFirst, below), so the
// host always processes what was typed before the action that depends on it — without this, a
// change event triggered immediately after typing reaches the host BEFORE the coalesced input,
// and any validator the change kicks off reads the stale model value.
const inputPending = new Set<ValueElement>();
let inputRaf = 0;

// TYPING FASTER THAN .NET ANSWERS. Every value sent is a handler and a render, one after the other, and on
// a heavy page that is slower than a key: eight letters typed at once used to be eight renders queued behind
// each other, the field's answer seconds behind the reader, each stale one written into a field already
// left. So while .NET still owes an element the answer to a value, what is typed into it is HELD, and only
// the latest value goes — when the answer arrives, or at once before any event that has to come after it
// (flushInputsNow). Only for a host that says when it has answered (rask-host.ts); the Server's socket
// does not, and every value is sent as before.
const unanswered = new Map<ValueElement, number>();
// What each held field said when it was last typed into. Kept, not read back when it is sent: the answer to
// an older value may have been written into a field already left, and that is not what was typed.
const held = new Map<ValueElement, string>();

function answered(el: ValueElement): void {
    const left = (unanswered.get(el) || 1) - 1;
    if (left > 0) {
        unanswered.set(el, left);
        return;
    }
    unanswered.delete(el);
    const value = held.get(el);
    if (value !== undefined) {
        held.delete(el);
        sendOwn(function () { dispatchInput(el, value); });
    }
}

function dispatchInput(el: ValueElement, value: string): void {
    if (!el.isConnected) return;
    const id = el.getAttribute("data-rask-on-input");
    if (!id) return;
    const answer = send({ id, type: "input", value });
    if (answer instanceof Promise) {
        unanswered.set(el, (unanswered.get(el) || 0) + 1);
        const done = () => answered(el);
        answer.then(done, done);
    }
}

function sendInput(el: ValueElement): void {
    if (unanswered.has(el)) held.set(el, el.value);
    else dispatchInput(el, el.value);
}

// A BOUND CONTROL SAYS NOTHING UNTIL THE NEXT ACTION. That is `data-rask-bind-on="action"` beside
// data-rask-on-change, and it is what a plain `.Bind(…)` renders, as Livewire's `wire:model` does: typing into
// it, choosing in it and leaving it send nothing. The control is remembered here instead, and the next thing a
// host sends — a click, a submit, a key a handler hears, another control's change, a navigation, anything a
// behaviour hook dispatches — is preceded by the change of every control remembered, in the order they were
// last touched (sendTypedFirst). `.Blur()` renders data-rask-bind-on="blur": remembered the same way, and
// sent by the `change` the browser fires on leaving it, whichever comes first. `.Live()` and `.Debounce(…)`
// render data-rask-debounce="<ms>" beside data-rask-on-input: what is typed is sent once typing has paused
// that long, or ahead of whatever follows.
const PAUSE = "data-rask-debounce";
const BIND_ON = "data-rask-bind-on";
// A paused field and its timer — 0 while a character is being composed, when no pause is counted.
const paused = new Map<ValueElement, number>();
// Controls that wait (for an action, or to be left) and have been changed since they last spoke.
const unsent = new Set<ValueElement>();
// What a field bound on blur was sent as, ahead of the `change` the browser has still to fire for it.
const committed = new WeakMap<Element, string>();

/** Whether a control's own `change` is not sent: it waits for the next action. */
export function waitsForAction(el: Element | null): boolean {
    return !!el && el.getAttribute(BIND_ON) === "action";
}

// Remembered last, whatever it was before: a radio pressed again after its neighbour is the group's answer.
// What the server had rendered is noted the first time (rask-morph.ts), so a render that knows nothing newer —
// a push, the reply to another event — does not write it back over what the reader chose.
/** Notes a control that waits as changed: its value goes ahead of the next thing sent. */
export function remember(el: ValueElement): void {
    raskNoteDirtyField(el);
    if (!unsent.delete(el)) raskNotePendingFormState(el);
    unsent.add(el);
}

/** The frame a control's `change` travels as. Both hosts send this one, and so does a control that waited. */
export function changeFrame(el: Element): Record<string, unknown> {
    const field = el as ValueElement;
    // What a lagging render would have to carry to be stale, recorded before the echo rewrites it.
    raskNoteDirtyField(field);
    raskNotePendingFormState(field);
    const frame: Record<string, unknown> = {
        id: field.getAttribute("data-rask-on-change"), type: "change", value: raskChangeFrameValue(field),
    };
    // Only a <select multiple> has more than one value, and its `.value` is the first of them.
    const values = raskChangeFrameValues(field);
    if (values !== null) frame.values = values;
    return frame;
}

function commit(el: ValueElement): void {
    if (!el.isConnected || !el.hasAttribute("data-rask-on-change")) return;
    // A radio that was pressed and then left for another one has nothing to say: the one now checked does.
    if ((el as HTMLInputElement).type === "radio" && !(el as HTMLInputElement).checked) return;
    committed.set(el, el.value);
    send(changeFrame(el));
}

function commitUnsent(): void {
    if (unsent.size === 0) return;
    const waiting = Array.from(unsent);
    unsent.clear();
    waiting.forEach(commit);
}

/** Whether this field's value already went, ahead of the `change` now being heard for it. */
export function changeSent(el: Element): boolean {
    const sent = committed.get(el) === (el as ValueElement).value;
    committed.delete(el);
    return sent;
}

// Every send below passes through the host, which asks for what waits to go first (sendTypedFirst). While
// this module is itself sending, that has been seen to already: nothing is flushed from inside a flush.
let sending = false;

function sendOwn(run: () => void): void {
    if (sending) {
        run();
        return;
    }
    sending = true;
    try {
        commitUnsent();
        run();
    } finally {
        sending = false;
    }
}

function pause(el: ValueElement): void {
    clearTimeout(paused.get(el));
    paused.set(el, window.setTimeout(function () {
        paused.delete(el);
        sendOwn(function () { sendInput(el); });
    }, Number(el.getAttribute(PAUSE))));
}

function flushInputs(): void {
    inputRaf = 0;
    sendOwn(function () {
        inputPending.forEach(sendInput);
        inputPending.clear();
    });
}

export function flushInputsNow(): void {
    if (sending) return;
    if (inputRaf) {
        cancelAnimationFrame(inputRaf);
        inputRaf = 0;
    }
    sendOwn(function () {
        inputPending.forEach(sendInput);
        inputPending.clear();
        paused.forEach(function (timer, el) {
            clearTimeout(timer);
            sendInput(el);
        });
        paused.clear();
        if (held.size > 0) {
            // What follows — a key, a click, the change — must find the page knowing what was typed.
            const waiting = Array.from(held);
            held.clear();
            waiting.forEach(function (entry) { dispatchInput(entry[0], entry[1]); });
        }
    });
}

/**
 * What a host calls with everything it is about to send, before it sends it: what the reader typed or chose
 * and has not been sent goes first, so no handler runs against a model that has not heard it.
 *
 * The reply to an interop call is not the reader's doing — the server asked — and carries nothing ahead of it.
 */
export function sendTypedFirst(payload: unknown): void {
    if (sending || (payload as { type?: unknown } | null)?.type === "jsResult") return;
    flushInputsNow();
}

function queueInput(el: ValueElement): void {
    inputPending.add(el);
    if (!inputRaf) inputRaf = requestAnimationFrame(flushInputs);
}

document.addEventListener("input", (e) => {
    // `e.target` is EventTarget on the base Event, and only an Element has closest(). The guard is
    // what makes that narrowing true rather than assumed — a composed event from inside a shadow
    // root can retarget to something that is not an Element at all.
    const target = e.target;
    if (!(target instanceof Element)) return;

    // data-rask-on-edit: a field that waits is showing a message about a value the reader has just begun to
    // change. The page hears that once — the attribute goes with it — and takes the message away.
    const edited = target.closest("[data-rask-on-edit]");
    if (edited && inRoot(edited)) {
        // Sent alone: it says a message is stale, and carries no value ahead of it.
        const id = edited.getAttribute("data-rask-on-edit");
        edited.removeAttribute("data-rask-on-edit");
        sending = true;
        try {
            send({ id, type: "edit" });
        } finally {
            sending = false;
        }
    }

    const waiting = target.closest<ValueElement>("[" + BIND_ON + "]");
    if (waiting && inRoot(waiting)) {
        committed.delete(waiting);
        remember(waiting);
        return;
    }

    const t = target.closest<ValueElement>("[data-rask-on-input]");
    if (!t || !inRoot(t)) return;

    // Mark the field user-edited and capture what the server had rendered for it, BEFORE the dispatch
    // below causes an echo that rewrites the `value` attribute. Only the Server runtime reads this
    // (its redeploy reload re-applies edited fields); it costs one WeakMap probe everywhere else.
    raskNoteDirtyField(t);

    // Inputs paired with data-rask-on-change need to dispatch SYNCHRONOUSLY: the change
    // event typically fires in the same task (Playwright fill, browser commit on blur),
    // and a downstream validator triggered by change reads the model state set by the
    // matching input. Coalescing the input would put the change event ahead of it on
    // the .NET dispatcher and the validator would observe stale state. Only standalone
    // input handlers (no change wired) get the rAF coalescing win.
    // Tested first: a field that waits for a pause is not sent at the keystroke, change handler or not. Nor
    // is a pause counted while a character is being composed — `compositionend` starts it.
    if (t.hasAttribute(PAUSE)) {
        if ((e as InputEvent).isComposing) {
            clearTimeout(paused.get(t));
            paused.set(t, 0);
        } else {
            pause(t);
        }
        return;
    }

    if (t.hasAttribute("data-rask-on-change")) {
        sendOwn(function () { sendInput(t); });
        return;
    }

    queueInput(t);
});

document.addEventListener("compositionend", (e) => {
    const t = e.target as ValueElement;
    if (paused.get(t) === 0) pause(t);
});

// Ahead of the hosts' own `change` listeners. A control that waits for an action is remembered and not sent —
// a checkbox, a select and a date say `change` where a text field says `input`. A field bound on blur is the
// hosts' to send, as any change is. A paused field has no change handler, so leaving it sends what the pause
// was still holding — and the frame that was already on its way is told what it must not write back
// (rask-morph.ts).
document.addEventListener("change", (e) => {
    const t = e.target as ValueElement;
    if (waitsForAction(t)) {
        if (inRoot(t)) remember(t);
        return;
    }
    if (unsent.delete(t) || !paused.has(t)) return;
    raskNotePendingFormState(t);
    flushInputsNow();
}, true);

// scroll events don't bubble — listen in capture phase at the document level so we
// observe scroll on any descendant with [data-rask-on-scroll]. Coalesce bursts via
// rAF: one outgoing message per frame per element, even if scroll fires 5–10x.
const scrollPending = new Set<Element>();
let scrollRaf = 0;

function flushScroll(): void {
    scrollRaf = 0;
    scrollPending.forEach((el) => {
        if (!el.isConnected) return;
        const id = el.getAttribute("data-rask-on-scroll");
        if (!id) return;
        // The scroll box travels as e.Target, the way MDN's JavaScript reads it (e.target.scrollTop).
        send({ id, type: "scroll", target: raskTargetState(el, 1) });
    });
    scrollPending.clear();
}

document.addEventListener("scroll", (e) => {
    const t = e.target;
    if (!(t instanceof Element)) return;
    if (!t.hasAttribute("data-rask-on-scroll")) return;
    if (!inRoot(t)) return;

    scrollPending.add(t);
    if (!scrollRaf) scrollRaf = requestAnimationFrame(flushScroll);
}, true);
