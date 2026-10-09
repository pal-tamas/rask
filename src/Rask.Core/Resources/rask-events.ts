// The extended GlobalEventHandlers delegation, shared by both client runtimes.
//
// Imported by the Server runtime (rask.ts) and the WASM runtime (rask.wasm.ts) so the two clients can
// never drift. What it needs from its host — `send(payload)` and `inRoot(el)` — used to be "symbols
// both hosts define in the surrounding scope"; they are imports now, which is the difference between
// a convention and a contract.
//
// Model: one capture-phase document listener per event routes to the nearest ancestor carrying
// `data-rask-on-<event>`, then ships the event's MDN fields — read by the table generated from MDN's data —
// tagged with that element's handler id. What is Rask's own is the handful of listeners below that do more
// than read an event: enter/leave simulation, drag seeding and dedupe, the keyboard's input flush.

// --- Per-category payload builders. Each maps a DOM event to the flat object its C# *EventArgs.FromJson
//     reads. Keys mirror the DOM property names so the readers stay one-liners. ---

import { inRoot, send } from "./rask-host.js";
import { closestFrom } from "./rask-morph.js";
import { flushInputsNow } from "./rask-input.js";
import { domEvents } from "./generated/rask-dom-events.js";
import { raskDomPayload } from "./rask-dom-payload.js";

// Events with a listener of their own below or in a host: click (the hosts' submit/popover/loading guards),
// keydown/keyup (the input flush), the drag four (seeding, drop-target marking, dedupe), scroll (coalesced in
// rask-input), and enter/leave (simulated, below).
var raskOwnListener = new Set(["click", "keydown", "keyup", "dragstart", "dragover", "drop", "dragend", "scroll",
    "mouseenter", "mouseleave", "pointerenter", "pointerleave"]);

// Every other event, straight from the table. One capture-phase listener each, routing to the nearest element
// carrying `data-rask-on-<event>`. An event that does not bubble (focus, toggle, a dialog's close, every media
// event) is delivered only to the element it fired ON, as the DOM itself delivers it: capture-phase delegation
// would otherwise hand an ancestor every descendant's event. Passive whenever Rask does not prevent the default.
domEvents.forEach(function (row) {
    var name = row[0], bubbles = row[2], prevent = row[3], attr = "data-rask-on-" + name;
    if (raskOwnListener.has(name)) { return; }
    document.addEventListener(name, function (e) {
        var target = closestFrom(e.target, "[" + attr + "]");
        if (!target || !inRoot(target)) { return; }
        if (!bubbles && target !== e.target) { return; }
        if (prevent) { e.preventDefault(); }
        var msg = raskDomPayload(e, name);
        msg.id = target.getAttribute(attr);
        msg.type = name;
        send(msg);
    }, { capture: true, passive: !prevent });
});

// mouseenter/leave and pointerenter/leave don't propagate to ancestors (not even in the capture phase),
// so a delegated listener can't observe them. Simulate via the bubbling over/out events plus a
// relatedTarget boundary check: fire only when the pointer truly crossed the element's outer edge
// (relatedTarget outside the element), not when moving between its own descendants.
function raskEnterLeave(sourceEvent: string, name: string): void {
    var attr = "data-rask-on-" + name;
    document.addEventListener(sourceEvent, function (e) {
        var target = closestFrom(e.target, "[" + attr + "]");
        if (!target || !inRoot(target)) { return; }
        var related = (e as MouseEvent).relatedTarget;
        if (related instanceof Node && target.contains(related)) { return; }
        var msg = raskDomPayload(e, name);
        msg.id = target.getAttribute(attr);
        msg.type = name;
        send(msg);
    }, { capture: true, passive: true });
}

raskEnterLeave("mouseover", "mouseenter");
raskEnterLeave("mouseout", "mouseleave");
raskEnterLeave("pointerover", "pointerenter");
raskEnterLeave("pointerout", "pointerleave");

// ----- Drag & drop -----------------------------------------------------------
// HTML5 native DnD. Each message carries the DragEvent's MDN fields (a handler may ignore them — the
// dragged item's identity usually rides the handler's closure).
// dragstart seeds dataTransfer so the drag is valid in Firefox; dragover must preventDefault on a
// drop target or the browser rejects the drop. The optional data-rask-on-dragover round-trip
// drives a server-rendered drop-target highlight — deduped to one message per hovered element.
// (drag/dragenter/dragleave come from the generated table above.)
var lastDragOverEl: Element | null = null;

document.addEventListener("dragstart", function (e) {
    var t = closestFrom(e.target, "[data-rask-on-dragstart]");
    if (!t || !inRoot(t)) { return; }
    if (e.dataTransfer) {
        try {
            e.dataTransfer.setData("text/plain", "");
        } catch (err) { /* some browsers throw if setData is disallowed — ignore */ }
        e.dataTransfer.effectAllowed = "move";
    }
    lastDragOverEl = null;
    send(Object.assign(raskDomPayload(e, "dragstart"), {id: t.getAttribute("data-rask-on-dragstart"), type: "dragstart"}));
});

document.addEventListener("dragover", function (e) {
    var t = closestFrom(e.target, "[data-rask-on-drop], [data-rask-on-dragover]");
    if (!t || !inRoot(t)) { return; }
    // preventDefault is what marks this element as a valid drop target.
    e.preventDefault();
    if (e.dataTransfer) { e.dataTransfer.dropEffect = "move"; }
    if (!t.hasAttribute("data-rask-on-dragover")) { return; }
    if (t === lastDragOverEl) { return; } // dedupe: only notify when the hovered target changes
    lastDragOverEl = t;
    send(Object.assign(raskDomPayload(e, "dragover"), {id: t.getAttribute("data-rask-on-dragover"), type: "dragover"}));
});

document.addEventListener("drop", function (e) {
    var t = closestFrom(e.target, "[data-rask-on-drop]");
    if (!t || !inRoot(t)) { return; }
    e.preventDefault();
    lastDragOverEl = null;
    send(Object.assign(raskDomPayload(e, "drop"), {id: t.getAttribute("data-rask-on-drop"), type: "drop"}));
});

document.addEventListener("dragend", function (e) {
    lastDragOverEl = null;
    var t = closestFrom(e.target, "[data-rask-on-dragend]");
    if (!t || !inRoot(t)) { return; }
    send(Object.assign(raskDomPayload(e, "dragend"), {id: t.getAttribute("data-rask-on-dragend"), type: "dragend"}));
});

// ----- Keyboard --------------------------------------------------------------
// keydown/keyup dispatch to the nearest ancestor carrying a handler (focus-scoped, like click).
// Never preventDefault — a key handler composes with normal typing; the C# side decides what a key
// means. flushInputsNow() first so an Enter-to-submit handler reads the value the user just typed, not the
// pre-flush one. The KeyboardEvent's MDN fields ride along (key, code, repeat, the modifier keys, …).
function raskSendKey(e: KeyboardEvent, attr: string, type: string): void {
    var t = closestFrom(e.target, "[" + attr + "]");
    if (!t || !inRoot(t)) { return; }
    // data-rask-keys="ArrowDown ArrowUp Enter Escape Tab": the only keys this element's handler hears, by
    // KeyboardEvent.key. A combobox acts on five; every letter typed into it was otherwise a round trip and a
    // render that changed nothing, ahead of the `input` that does.
    var keys = t.getAttribute("data-rask-keys");
    if (keys !== null && keys.split(" ").indexOf(e.key) < 0) { return; }
    flushInputsNow();
    send(Object.assign(raskDomPayload(e, type), {id: t.getAttribute(attr), type: type}));
}

document.addEventListener("keydown", function (e) { raskSendKey(e, "data-rask-on-keydown", "keydown"); });
document.addEventListener("keyup", function (e) { raskSendKey(e, "data-rask-on-keyup", "keyup"); });

// ----- Share (client-only) ---------------------------------------------------
// ShareButton emits data-rask-share="{json}". The share MUST run inside the click's own call stack so the
// browser's transient user activation is still live — a server round-trip would lose it, which is exactly
// why this is handled on the client and not dispatched to C#.
// Unsupported browsers (e.g. desktop Firefox) simply no-op.
document.addEventListener("click", function (e) {
    var t = closestFrom(e.target, "[data-rask-share]");
    if (!t || !inRoot(t)) { return; }
    var raw = t.getAttribute("data-rask-share");
    if (!raw) { return; }
    if (navigator.share) {
        var data;
        try { data = JSON.parse(raw); } catch (err) { return; }
        // Fire in the gesture; swallow rejections (user cancel / unsupported payload).
        try { var p = navigator.share(data); if (p && p["catch"]) { p["catch"](function () {}); } } catch (err) {}
    }
});

// ----- Gesture bridge (client-only) ------------------------------------------
// GestureTrigger / FullscreenTrigger / EyeDropperTrigger emit data-rask-gesture="{cap,rid}". The capability
// MUST run inside the click's own call stack so the browser's transient user activation is still live — a
// server round-trip would lose it. That's what lets activation-gated APIs (fullscreen, eyedropper, …) work
// even on the Server transport. When a result-callback id (rid) is set, the resolved value is posted back to
// C# via the shared DotNet shim (static [JSInvokable] GestureResultInterop.Result in Rask.Core).
// Each cap runs synchronously inside the click, given (arg, el): arg is the payload's optional string
// argument (orientation type, JSON media constraints), el the resolved target element (the <video> for
// picture-in-picture / media capture). A returned Promise's value is posted back when a rid is set.
var raskGestureCaps: Record<string, (arg: string | null, el: HTMLElement | null) => unknown> = {
    "fullscreen.request": function (_arg: string | null, el: HTMLElement | null) { return window.__raskFullscreen ? window.__raskFullscreen.request(el) : null; },
    "eyedropper.open": function () { return window.__raskEyeDropper ? window.__raskEyeDropper.open() : null; },
    "orientation.lock": function (arg: string | null) {
        // screen.orientation.lock only resolves while the page is fullscreen (and on a device that honours
        // it); off-fullscreen / on desktop it rejects, which the dispatcher swallows — a genuine silent
        // no-op. Pair with FullscreenTrigger (or app fullscreen) rather than forcing fullscreen here, which
        // would strand a desktop user in a fullscreen page with the orientation unchanged.
        return window.__raskOrientation ? window.__raskOrientation.lock(arg) : null;
    },
    "pip.request": function (_arg: string | null, el: HTMLElement | null) { return window.__raskPip ? window.__raskPip.request(el) : null; },
    "install.prompt": function () {
        return window.__raskInstall ? window.__raskInstall.prompt() : Promise.resolve("unavailable");
    },
    "media.start": function (arg: string | null, el: HTMLElement | null) {
        if (!window.__raskMedia || !el) { return Promise.resolve("denied"); }
        var c;
        try { c = arg ? JSON.parse(arg) : {}; } catch { c = {}; }
        const media = window.__raskMedia;
        return media.getUserMedia(c).then(function (stream: MediaStream) {
            // Await the attach/play so a resolved id reflects a stream actually running in the <video>, not just
            // permission; a play() hiccup on a muted stream still counts as granted (permission was given). Resolves
            // the id the stream is handed over under rather than the literal "granted": MediaCaptureTrigger maps it
            // back to "granted" for OnResult, and takes the stream it names for OnStream, as a handle C# keeps.
            const handed = function () { return String(media.hand(stream)); };
            return Promise.resolve(media.attach(stream, el)).then(handed, handed);
        }, function () { return "denied"; });
    }
};
/** Whether a capability returned something awaitable, narrowed so `.then` is reachable. */
function isThenable(v: unknown): v is PromiseLike<unknown> {
    return !!v && typeof (v as PromiseLike<unknown>).then === "function";
}

function raskPostGestureResult(rid: string | number | null | undefined, value: unknown): void {
    if (window.DotNet && window.DotNet.invokeMethodAsync) {
        window.DotNet.invokeMethodAsync("Rask.Core", "RaskGestureResult", rid, value == null ? null : value);
    }
}
document.addEventListener("click", function (e) {
    var t = closestFrom(e.target, "[data-rask-gesture]");
    if (!t || !inRoot(t)) { return; }
    var raw = t.getAttribute("data-rask-gesture");
    if (!raw) { return; }
    var spec: RaskGestureSpec;
    try { spec = JSON.parse(raw) as RaskGestureSpec; } catch { return; }
    var run = raskGestureCaps[spec.cap];
    if (!run) { return; }
    // Resolve an optional target element from its ElementRef id (data-rask-ref), same selector the ref reviver uses.
    var el = spec.el ? document.querySelector<HTMLElement>('[data-rask-ref="' + spec.el + '"]') : null;
    var result;
    try { result = run(spec.arg ?? null, el); } catch { if (spec.rid != null) { raskPostGestureResult(spec.rid, null); } return; }
    if (spec.rid != null) {
        // Always post back when a result is expected, so the one-shot server-side handler is consumed
        // (never left dangling) — even if the cap returned a non-thenable (e.g. an unavailable capability).
        //
        // Tested inline rather than through a boolean: a narrowing does not survive being stored in
        // one, so `result.then` would still be a call on `unknown`.
        if (isThenable(result)) {
            result.then(function (value: unknown) { raskPostGestureResult(spec.rid, value); },
                function () { raskPostGestureResult(spec.rid, null); });
        } else {
            raskPostGestureResult(spec.rid, result == null ? null : result);
        }
    } else if (isThenable(result)) {
        // No result expected: swallow a rejection so an unhandled promise never reaches the console
        // for a capability the caller did not ask about. Promise.resolve wraps a bare thenable, which
        // is not required to carry `catch`.
        Promise.resolve(result).catch(function () {});
    }
});
