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
import { raskNoteDirtyField, raskNotePendingFormState } from "./rask-morph.js";
import { raskTargetState } from "./rask-dom-payload.js";

/** Any element this module reads a `value` off. */
type ValueElement = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement;

// Input events fire per keystroke — on fast typing that's 5–10 messages over the
// transport per second per input. Coalesce per-element with rAF: the same element typed into
// multiple times within one frame produces a single outgoing message carrying the latest value
// at flush time. The element itself is the de-duping key — multiple inputs in the same frame
// each get one message. flushInputsNow() is called at the top of every other event handler
// (change, submit, click, navigate, keydown) so the host always processes input events before
// the subsequent action that depends on them — without this, a change event triggered
// immediately after typing reaches the host BEFORE the coalesced input, and any validator the
// change kicks off reads the stale model value.
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
        dispatchInput(el, value);
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

// A FIELD THAT WAITS. `.Debounce(…)` renders data-rask-debounce="<ms>" beside data-rask-on-input: what is
// typed is sent once typing has paused that long. `.Blur()` renders data-rask-bind-on beside
// data-rask-on-change: it is sent by the `change` the browser fires on leaving. Either way, whatever has to
// come after the value — Enter's submit, a click or key handler, a navigation — sends it first
// (flushInputsNow), and a `change` fired afterwards for a value already sent is not sent again.
const PAUSE = "data-rask-debounce";
// A paused field and its timer — 0 while a character is being composed, when no pause is counted.
const paused = new Map<ValueElement, number>();
// Fields bound on blur that were typed into and have said nothing yet.
const unsent = new Set<ValueElement>();
// What a field bound on blur was sent as, ahead of the `change` the browser has still to fire for it.
const committed = new WeakMap<Element, string>();

function pause(el: ValueElement): void {
    clearTimeout(paused.get(el));
    paused.set(el, window.setTimeout(function () {
        paused.delete(el);
        sendInput(el);
    }, Number(el.getAttribute(PAUSE))));
}

function commit(el: ValueElement): void {
    const id = el.getAttribute("data-rask-on-change");
    if (!id || !el.isConnected) return;
    committed.set(el, el.value);
    send({ id, type: "change", value: el.value });
}

/** Whether this field's value already went, ahead of the `change` now being heard for it. */
export function changeSent(el: Element): boolean {
    const sent = committed.get(el) === (el as ValueElement).value;
    committed.delete(el);
    return sent;
}

function flushInputs(): void {
    inputRaf = 0;
    inputPending.forEach(sendInput);
    inputPending.clear();
}

export function flushInputsNow(): void {
    if (inputRaf) {
        cancelAnimationFrame(inputRaf);
        inputRaf = 0;
    }
    if (inputPending.size > 0) flushInputs();
    paused.forEach(function (timer, el) {
        clearTimeout(timer);
        sendInput(el);
    });
    paused.clear();
    unsent.forEach(commit);
    unsent.clear();
    if (held.size > 0) {
        // What follows — a key, a click, the change — must find the page knowing what was typed.
        const waiting = Array.from(held);
        held.clear();
        waiting.forEach(function (entry) { dispatchInput(entry[0], entry[1]); });
    }
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
        send({ id: edited.getAttribute("data-rask-on-edit"), type: "edit" });
        edited.removeAttribute("data-rask-on-edit");
    }

    const leaving = target.closest<ValueElement>("[data-rask-bind-on]");
    if (leaving && inRoot(leaving)) {
        raskNoteDirtyField(leaving);
        committed.delete(leaving);
        unsent.add(leaving);
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
        sendInput(t);
        return;
    }

    queueInput(t);
});

document.addEventListener("compositionend", (e) => {
    const t = e.target as ValueElement;
    if (paused.get(t) === 0) pause(t);
});

// Ahead of the hosts' own `change` listeners. A field bound on blur is theirs to send, as any change is. A
// paused field has no change handler, so leaving it sends what the pause was still holding — and the frame
// that was already on its way is told what it must not write back (rask-morph.ts).
document.addEventListener("change", (e) => {
    const t = e.target as ValueElement;
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
