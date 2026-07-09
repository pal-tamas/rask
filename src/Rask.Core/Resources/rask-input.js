// rask-input.js — rAF-coalesced input & scroll dispatch, shared by all three client runtimes.
//
// Spliced (at "// @@RASK_INPUT@@") into the Server runtime (rask.js), the WASM runtime
// (rask.wasm.js) and the native runtime (rask.native.js) so the three clients can never drift.
// It relies only on three symbols every host defines in the surrounding scope: `send(payload)`,
// `inRoot(el)` and the global `document` (plus the standard requestAnimationFrame/
// cancelAnimationFrame). This module MUST be spliced BEFORE rask-events.js, whose keyboard handler
// calls flushInputsNow().
//
// Written in modern-ES (const/let/arrow), matching rask-dom.js / rask-morph.js — the other shared
// modules already spliced into all three hosts. No export/import, no backslash regex literals (the
// splice is a raw string .Replace).

// Input events fire per keystroke — on fast typing that's 5–10 messages over the
// transport per second per input. Coalesce per-element with rAF: the same element typed into
// multiple times within one frame produces a single outgoing message carrying the latest value
// at flush time. The element itself is the de-duping key — multiple inputs in the same frame
// each get one message. flushInputsNow() is called at the top of every other event handler
// (change, submit, click, navigate, keydown) so the host always processes input events before
// the subsequent action that depends on them — without this, a change event triggered
// immediately after typing reaches the host BEFORE the coalesced input, and any validator the
// change kicks off reads the stale model value.
const inputPending = new Set();
let inputRaf = 0;

function flushInputs() {
    inputRaf = 0;
    inputPending.forEach((el) => {
        if (!el.isConnected) return;
        const id = el.getAttribute("data-rask-on-input");
        if (!id) return;
        send({id, type: "input", value: el.value});
    });
    inputPending.clear();
}

function flushInputsNow() {
    if (inputRaf) {
        cancelAnimationFrame(inputRaf);
        inputRaf = 0;
    }
    if (inputPending.size > 0) flushInputs();
}

function queueInput(el) {
    inputPending.add(el);
    if (!inputRaf) inputRaf = requestAnimationFrame(flushInputs);
}

document.addEventListener("input", (e) => {
    const t = e.target.closest("[data-rask-on-input]");
    if (!t || !inRoot(t)) return;
    // Inputs paired with data-rask-on-change need to dispatch SYNCHRONOUSLY: the change
    // event typically fires in the same task (Playwright fill, browser commit on blur),
    // and a downstream validator triggered by change reads the model state set by the
    // matching input. Coalescing the input would put the change event ahead of it on
    // the .NET dispatcher and the validator would observe stale state. Only standalone
    // input handlers (no change wired) get the rAF coalescing win.
    if (t.hasAttribute("data-rask-on-change")) {
        send({id: t.getAttribute("data-rask-on-input"), type: "input", value: t.value});
        return;
    }
    queueInput(t);
});

// scroll events don't bubble — listen in capture phase at the document level so we
// observe scroll on any descendant with [data-rask-on-scroll]. Coalesce bursts via
// rAF: one outgoing message per frame per element, even if scroll fires 5–10x.
const scrollPending = new Set();
let scrollRaf = 0;

function flushScroll() {
    scrollRaf = 0;
    scrollPending.forEach((el) => {
        if (!el.isConnected) return;
        const id = el.getAttribute("data-rask-on-scroll");
        if (!id) return;
        send({
            id,
            type: "scroll",
            scrollTop: el.scrollTop | 0,
            clientHeight: el.clientHeight | 0,
            scrollHeight: el.scrollHeight | 0
        });
    });
    scrollPending.clear();
}

document.addEventListener("scroll", (e) => {
    const t = e.target;
    if (!t || t.nodeType !== 1) return;
    if (!t.hasAttribute || !t.hasAttribute("data-rask-on-scroll")) return;
    if (!inRoot(t)) return;
    scrollPending.add(t);
    if (!scrollRaf) scrollRaf = requestAnimationFrame(flushScroll);
}, true);
