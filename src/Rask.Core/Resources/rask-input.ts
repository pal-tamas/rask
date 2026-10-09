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
import { raskNoteDirtyField } from "./rask-morph.js";
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
const held = new Set<ValueElement>();

function answered(el: ValueElement): void {
    const left = (unanswered.get(el) || 1) - 1;
    if (left > 0) {
        unanswered.set(el, left);
        return;
    }
    unanswered.delete(el);
    if (held.delete(el)) dispatchInput(el);
}

function dispatchInput(el: ValueElement): void {
    if (!el.isConnected) return;
    const id = el.getAttribute("data-rask-on-input");
    if (!id) return;
    const answer = send({ id, type: "input", value: el.value });
    if (answer instanceof Promise) {
        unanswered.set(el, (unanswered.get(el) || 0) + 1);
        const done = () => answered(el);
        answer.then(done, done);
    }
}

function sendInput(el: ValueElement): void {
    if (unanswered.has(el)) held.add(el);
    else dispatchInput(el);
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
    if (held.size > 0) {
        // What follows — a key, a click, the change — must find the page knowing what was typed.
        const waiting = Array.from(held);
        held.clear();
        waiting.forEach(dispatchInput);
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
    if (t.hasAttribute("data-rask-on-change")) {
        sendInput(t);
        return;
    }

    queueInput(t);
});

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
