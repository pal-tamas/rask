// The extended GlobalEventHandlers delegation, shared by both client runtimes.
//
// Imported by the Server runtime (rask.ts) and the WASM runtime (rask.wasm.ts) so the two clients can
// never drift. What it needs from its host — `send(payload)` and `inRoot(el)` — used to be "symbols
// both hosts define in the surrounding scope"; they are imports now, which is the difference between
// a convention and a contract.
//
// Model: one capture-phase document listener per event routes to the nearest ancestor carrying
// `data-rask-on-<event>`, then ships a per-category JSON payload tagged with that element's handler id.
// Capture phase is used so non-bubbling events (focus/blur) still reach the delegated listener. Click,
// scroll and input/change/submit keep their own dedicated listeners in each host (their coalescing /
// form / file behaviour is host-specific) — this file covers everything else: mouse, pointer, touch,
// wheel, focus, clipboard, the HTMLMediaElement events, AND (see the tail of this file) keyboard
// (keydown/keyup) + the four core drag events (dragstart/dragover/drop/dragend), which used to be
// hand-copied into each host. Written defensively: every builder tolerates a partial event object.

// --- Per-category payload builders. Each maps a DOM event to the flat object its C# *EventArgs.FromJson
//     reads. Keys mirror the DOM property names so the readers stay one-liners. ---

import { inRoot, send } from "./rask-host.js";
import { closestFrom } from "./rask-morph.js";
import { flushInputsNow } from "./rask-input.js";

/**
 * The flat object a C# `*EventArgs.FromJson` reads.
 *
 * A loose record on purpose: the key set differs per event category, several builders start from
 * `raskMouse` and add their own fields, and the contract that matters is held on the C# side by the
 * reader for each category. Typing it as a union of eight exact shapes would describe this file's
 * internals rather than the agreement it is party to.
 */
type EventPayload = Record<string, unknown>;

/** Geometry + button + modifier state shared by every mouse/pointer event. */
function raskMouse(ev: Event): EventPayload {
    const e = ev as MouseEvent;
    return {
        button: e.button, buttons: e.buttons,
        clientX: e.clientX, clientY: e.clientY, screenX: e.screenX, screenY: e.screenY,
        pageX: e.pageX, pageY: e.pageY, offsetX: e.offsetX, offsetY: e.offsetY,
        movementX: e.movementX, movementY: e.movementY,
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    };
}

/** Mouse geometry + scroll deltas for the wheel event. */
function raskWheel(ev: Event): EventPayload {
    const e = ev as WheelEvent;
    var m = raskMouse(ev);
    m.deltaX = e.deltaX; m.deltaY = e.deltaY; m.deltaZ = e.deltaZ; m.deltaMode = e.deltaMode;
    return m;
}

/** Mouse geometry + pointer-device fields. */
function raskPointer(ev: Event): EventPayload {
    const e = ev as PointerEvent;
    var m = raskMouse(ev);
    m.pointerId = e.pointerId; m.width = e.width; m.height = e.height;
    m.pressure = e.pressure; m.tangentialPressure = e.tangentialPressure;
    m.tiltX = e.tiltX; m.tiltY = e.tiltY; m.twist = e.twist;
    m.pointerType = e.pointerType; m.isPrimary = e.isPrimary;
    return m;
}

/** Active-touch count + first-touch coordinates + modifiers. */
function raskTouch(ev: Event): EventPayload {
    const e = ev as TouchEvent;
    var list = (e.touches && e.touches.length) ? e.touches : e.changedTouches;
    var first = (list && list.length) ? list[0] : null;
    return {
        touchCount: e.touches ? e.touches.length : 0,
        clientX: first ? first.clientX : 0, clientY: first ? first.clientY : 0,
        pageX: first ? first.pageX : 0, pageY: first ? first.pageY : 0,
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    };
}

/** The plain-text clipboard payload, read while it's accessible during the event. */
function raskClipboard(ev: Event): EventPayload {
    const e = ev as ClipboardEvent;
    var text = "";
    try {
        var data = e.clipboardData || window.clipboardData;
        // window.clipboardData is the legacy IE path, declared in rask-window.d.ts because lib.dom
        // dropped it — kept because it costs one line and this runs inside a try anyway.
        if (data) { text = data.getData("text") || ""; }
    } catch { /* access blocked — leave text empty */ }
    return { text: text };
}

/** A snapshot of the media element's playback state (NaN/Infinity duration normalised to 0). */
function raskMedia(e: Event): EventPayload {
    // A media event's target is the media element; the fallback keeps every read below defined for a
    // synthetic event that carries none.
    var el = (e.target as HTMLMediaElement | null) ?? ({} as HTMLMediaElement);
    return {
        currentTime: el.currentTime || 0,
        duration: (el.duration && isFinite(el.duration)) ? el.duration : 0,
        paused: !!el.paused, ended: !!el.ended,
        volume: el.volume == null ? 1 : el.volume, muted: !!el.muted,
        playbackRate: el.playbackRate == null ? 1 : el.playbackRate
    };
}

/** The inserted text for beforeinput (surfaced to a Callback<string>). */
function raskBeforeInput(ev: Event): EventPayload { const e = ev as InputEvent; return { value: e.data == null ? "" : e.data }; }

/** Parameterless events (focus/blur, drag/dragenter/dragleave, select/invalid/reset). */
function raskNone(): EventPayload { return {}; }

// --- The registration table. Each row is [eventName, payloadBuilder, preventDefault]. ---
var raskDomEvents: [string, (e: Event) => EventPayload, boolean][] = [
    ["dblclick", raskMouse, false], ["mousedown", raskMouse, false], ["mouseup", raskMouse, false],
    ["mousemove", raskMouse, false], ["mouseover", raskMouse, false], ["mouseout", raskMouse, false],
    ["contextmenu", raskMouse, true],
    ["wheel", raskWheel, false],
    ["pointerdown", raskPointer, false], ["pointerup", raskPointer, false], ["pointermove", raskPointer, false],
    ["pointerover", raskPointer, false], ["pointerout", raskPointer, false], ["pointercancel", raskPointer, false],
    ["touchstart", raskTouch, false], ["touchend", raskTouch, false], ["touchmove", raskTouch, false], ["touchcancel", raskTouch, false],
    ["focus", raskNone, false], ["blur", raskNone, false], ["focusin", raskNone, false], ["focusout", raskNone, false],
    ["drag", raskNone, false], ["dragenter", raskNone, false], ["dragleave", raskNone, false],
    ["copy", raskClipboard, false], ["cut", raskClipboard, false], ["paste", raskClipboard, false],
    ["beforeinput", raskBeforeInput, false], ["select", raskNone, false], ["invalid", raskNone, false], ["reset", raskNone, false],
    ["play", raskMedia, false], ["pause", raskMedia, false], ["playing", raskMedia, false], ["ended", raskMedia, false],
    ["timeupdate", raskMedia, false], ["volumechange", raskMedia, false], ["ratechange", raskMedia, false],
    ["durationchange", raskMedia, false], ["loadedmetadata", raskMedia, false],
    ["seeked", raskMedia, false], ["seeking", raskMedia, false], ["waiting", raskMedia, false]
];

raskDomEvents.forEach(function (spec) {
    var name = spec[0], build = spec[1], prevent = spec[2], attr = "data-rask-on-" + name;
    // passive when we never preventDefault — lets the browser keep scrolling/painting smoothly even
    // while a high-frequency handler (mousemove/touchmove/wheel) is attached.
    document.addEventListener(name, function (e) {
        var target = closestFrom(e.target, "[" + attr + "]");
        if (!target || !inRoot(target)) { return; }
        if (prevent) { e.preventDefault(); }
        var msg = build(e);
        msg.id = target.getAttribute(attr);
        msg.type = name;
        send(msg);
    }, { capture: true, passive: !prevent });
});

// mouseenter/leave and pointerenter/leave don't propagate to ancestors (not even in the capture phase),
// so a delegated listener can't observe them. Simulate via the bubbling over/out events plus a
// relatedTarget boundary check: fire only when the pointer truly crossed the element's outer edge
// (relatedTarget outside the element), not when moving between its own descendants.
function raskEnterLeave(sourceEvent: string, name: string, build: (e: Event) => EventPayload): void {
    var attr = "data-rask-on-" + name;
    document.addEventListener(sourceEvent, function (e) {
        var target = closestFrom(e.target, "[" + attr + "]");
        if (!target || !inRoot(target)) { return; }
        var related = (e as MouseEvent).relatedTarget;
        if (related instanceof Node && target.contains(related)) { return; }
        var msg = build(e);
        msg.id = target.getAttribute(attr);
        msg.type = name;
        send(msg);
    }, { capture: true, passive: true });
}

raskEnterLeave("mouseover", "mouseenter", raskMouse);
raskEnterLeave("mouseout", "mouseleave", raskMouse);
raskEnterLeave("pointerover", "pointerenter", raskPointer);
raskEnterLeave("pointerout", "pointerleave", raskPointer);

// ----- Drag & drop -----------------------------------------------------------
// HTML5 native DnD bound to parameterless C# handlers (same dispatch path as click). The dragged
// item's identity rides the handler's closure, not the payload, so messages carry only {id,type}.
// dragstart seeds dataTransfer so the drag is valid in Firefox; dragover must preventDefault on a
// drop target or the browser rejects the drop. The optional data-rask-on-dragover round-trip
// drives a server-rendered drop-target highlight — deduped to one message per hovered element.
// (drag/dragenter/dragleave are covered by the parameterless table above.)
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
    send({id: t.getAttribute("data-rask-on-dragstart"), type: "dragstart"});
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
    send({id: t.getAttribute("data-rask-on-dragover"), type: "dragover"});
});

document.addEventListener("drop", function (e) {
    var t = closestFrom(e.target, "[data-rask-on-drop]");
    if (!t || !inRoot(t)) { return; }
    e.preventDefault();
    lastDragOverEl = null;
    send({id: t.getAttribute("data-rask-on-drop"), type: "drop"});
});

document.addEventListener("dragend", function (e) {
    lastDragOverEl = null;
    var t = closestFrom(e.target, "[data-rask-on-dragend]");
    if (!t || !inRoot(t)) { return; }
    send({id: t.getAttribute("data-rask-on-dragend"), type: "dragend"});
});

// ----- Keyboard --------------------------------------------------------------
// keydown/keyup dispatch to the nearest ancestor carrying a handler (focus-scoped, like click).
// Never preventDefault — a key handler composes with normal typing; the C# side decides what a key
// means. flushInputsNow() first (when present — rask-input.js is spliced ahead of this file) so an
// Enter-to-submit handler reads the value the user just typed, not the pre-flush one. Modifier flags
// + repeat ride along for shortcuts.
function raskSendKey(e: KeyboardEvent, attr: string, type: string): void {
    var t = closestFrom(e.target, "[" + attr + "]");
    if (!t || !inRoot(t)) { return; }
    flushInputsNow();
    send({
        id: t.getAttribute(attr), type: type,
        key: e.key, code: e.code, repeat: e.repeat,
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    });
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
        return media.getUserMedia(c).then(function (id: number) {
            // Await the attach/play so a resolved id reflects a stream actually running in the <video>, not
            // just permission; a play() hiccup on a muted stream still counts as granted (permission was
            // given). Resolves the stream's ID rather than the literal "granted": MediaCaptureTrigger maps
            // it back to "granted" for OnResult, and hands it to OnStream so a Server-hosted app can keep
            // the stream — stop it, re-attach it, or send it to a WebRTC peer. Before this the stream was
            // unreachable from C# on the Server host.
            return Promise.resolve(media.attach(id, el)).then(
                function () { return String(id); }, function () { return String(id); });
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
