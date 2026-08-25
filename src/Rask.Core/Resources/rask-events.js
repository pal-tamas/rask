// rask-events.js — the extended GlobalEventHandlers delegation, shared by both client runtimes.
//
// Spliced into the Server runtime (rask.js, at "// @@RASK_EVENTS@@") and the WASM runtime
// (rask.wasm.js) so the two clients can never drift. It relies only on three symbols that both hosts
// define in the surrounding scope: `send(payload)`, `inRoot(el)` and the global `document`.
//
// Model: one capture-phase document listener per event routes to the nearest ancestor carrying
// `data-rask-on-<event>`, then ships a per-category JSON payload tagged with that element's handler id.
// Capture phase is used so non-bubbling events (focus/blur) still reach the delegated listener. Click,
// scroll and input/change/submit keep their own dedicated listeners in each host (their coalescing /
// form / file behaviour is host-specific) — this file covers everything else: mouse, pointer, touch,
// wheel, focus, clipboard, the HTMLMediaElement events, AND (see the tail of this file) keyboard
// (keydown/keyup) + the four core drag events (dragstart/dragover/drop/dragend), which used to be
// hand-copied into each host. Kept ES5 (var/function) because it is spliced verbatim into all three
// hosts. Written defensively: every builder tolerates a partial event object.

// --- Per-category payload builders. Each maps a DOM event to the flat object its C# *EventArgs.FromJson
//     reads. Keys mirror the DOM property names so the readers stay one-liners. ---

/** Geometry + button + modifier state shared by every mouse/pointer event. */
function raskMouse(e) {
    return {
        button: e.button, buttons: e.buttons,
        clientX: e.clientX, clientY: e.clientY, screenX: e.screenX, screenY: e.screenY,
        pageX: e.pageX, pageY: e.pageY, offsetX: e.offsetX, offsetY: e.offsetY,
        movementX: e.movementX, movementY: e.movementY,
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    };
}

/** Mouse geometry + scroll deltas for the wheel event. */
function raskWheel(e) {
    var m = raskMouse(e);
    m.deltaX = e.deltaX; m.deltaY = e.deltaY; m.deltaZ = e.deltaZ; m.deltaMode = e.deltaMode;
    return m;
}

/** Mouse geometry + pointer-device fields. */
function raskPointer(e) {
    var m = raskMouse(e);
    m.pointerId = e.pointerId; m.width = e.width; m.height = e.height;
    m.pressure = e.pressure; m.tangentialPressure = e.tangentialPressure;
    m.tiltX = e.tiltX; m.tiltY = e.tiltY; m.twist = e.twist;
    m.pointerType = e.pointerType; m.isPrimary = e.isPrimary;
    return m;
}

/** Active-touch count + first-touch coordinates + modifiers. */
function raskTouch(e) {
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
function raskClipboard(e) {
    var text = "";
    try {
        var data = e.clipboardData || window.clipboardData;
        if (data) { text = data.getData("text") || ""; }
    } catch (err) { /* access blocked — leave text empty */ }
    return { text: text };
}

/** A snapshot of the media element's playback state (NaN/Infinity duration normalised to 0). */
function raskMedia(e) {
    var el = e.target || {};
    return {
        currentTime: el.currentTime || 0,
        duration: (el.duration && isFinite(el.duration)) ? el.duration : 0,
        paused: !!el.paused, ended: !!el.ended,
        volume: el.volume == null ? 1 : el.volume, muted: !!el.muted,
        playbackRate: el.playbackRate == null ? 1 : el.playbackRate
    };
}

/** The inserted text for beforeinput (surfaced to a Callback<string>). */
function raskBeforeInput(e) { return { value: e.data == null ? "" : e.data }; }

/** Parameterless events (focus/blur, drag/dragenter/dragleave, select/invalid/reset). */
function raskNone() { return {}; }

// --- The registration table. Each row is [eventName, payloadBuilder, preventDefault]. ---
var raskDomEvents = [
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
        var target = (e.target && e.target.closest) ? e.target.closest("[" + attr + "]") : null;
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
function raskEnterLeave(sourceEvent, name, build) {
    var attr = "data-rask-on-" + name;
    document.addEventListener(sourceEvent, function (e) {
        var target = (e.target && e.target.closest) ? e.target.closest("[" + attr + "]") : null;
        if (!target || !inRoot(target)) { return; }
        var related = e.relatedTarget;
        if (related && target.contains(related)) { return; }
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
var lastDragOverEl = null;

document.addEventListener("dragstart", function (e) {
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-dragstart]") : null;
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
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-drop], [data-rask-on-dragover]") : null;
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
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-drop]") : null;
    if (!t || !inRoot(t)) { return; }
    e.preventDefault();
    lastDragOverEl = null;
    send({id: t.getAttribute("data-rask-on-drop"), type: "drop"});
});

document.addEventListener("dragend", function (e) {
    lastDragOverEl = null;
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-dragend]") : null;
    if (!t || !inRoot(t)) { return; }
    send({id: t.getAttribute("data-rask-on-dragend"), type: "dragend"});
});

// ----- Keyboard --------------------------------------------------------------
// keydown/keyup dispatch to the nearest ancestor carrying a handler (focus-scoped, like click).
// Never preventDefault — a key handler composes with normal typing; the C# side decides what a key
// means. flushInputsNow() first (when present — rask-input.js is spliced ahead of this file) so an
// Enter-to-submit handler reads the value the user just typed, not the pre-flush one. Modifier flags
// + repeat ride along for shortcuts.
function raskSendKey(e, attr, type) {
    var t = (e.target && e.target.closest) ? e.target.closest("[" + attr + "]") : null;
    if (!t || !inRoot(t)) { return; }
    if (typeof flushInputsNow === "function") { flushInputsNow(); }
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
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-share]") : null;
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
var raskGestureCaps = {
    "fullscreen.request": function (arg, el) { return window.__raskFullscreen ? window.__raskFullscreen.request(el) : null; },
    "eyedropper.open": function () { return window.__raskEyeDropper ? window.__raskEyeDropper.open() : null; },
    "orientation.lock": function (arg) {
        // screen.orientation.lock only resolves while the page is fullscreen (and on a device that honours
        // it); off-fullscreen / on desktop it rejects, which the dispatcher swallows — a genuine silent
        // no-op. Pair with FullscreenTrigger (or app fullscreen) rather than forcing fullscreen here, which
        // would strand a desktop user in a fullscreen page with the orientation unchanged.
        return window.__raskOrientation ? window.__raskOrientation.lock(arg) : null;
    },
    "pip.request": function (arg, el) { return window.__raskPip ? window.__raskPip.request(el) : null; },
    "install.prompt": function () {
        return window.__raskInstall ? window.__raskInstall.prompt() : Promise.resolve("unavailable");
    },
    "media.start": function (arg, el) {
        if (!window.__raskMedia || !el) { return Promise.resolve("denied"); }
        var c;
        try { c = arg ? JSON.parse(arg) : {}; } catch (err) { c = {}; }
        return window.__raskMedia.getUserMedia(c).then(function (id) {
            // Await the attach/play so a resolved id reflects a stream actually running in the <video>, not
            // just permission; a play() hiccup on a muted stream still counts as granted (permission was
            // given). Resolves the stream's ID rather than the literal "granted": MediaCaptureTrigger maps
            // it back to "granted" for OnResult, and hands it to OnStream so a Server-hosted app can keep
            // the stream — stop it, re-attach it, or send it to a WebRTC peer. Before this the stream was
            // unreachable from C# on the Server host.
            return Promise.resolve(window.__raskMedia.attach(id, el)).then(
                function () { return String(id); }, function () { return String(id); });
        }, function () { return "denied"; });
    }
};
function raskPostGestureResult(rid, value) {
    if (window.DotNet && window.DotNet.invokeMethodAsync) {
        window.DotNet.invokeMethodAsync("Rask.Core", "RaskGestureResult", rid, value == null ? null : value);
    }
}
document.addEventListener("click", function (e) {
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-gesture]") : null;
    if (!t || !inRoot(t)) { return; }
    var raw = t.getAttribute("data-rask-gesture");
    if (!raw) { return; }
    var spec;
    try { spec = JSON.parse(raw); } catch (err) { return; }
    var run = raskGestureCaps[spec.cap];
    if (!run) { return; }
    // Resolve an optional target element from its ElementRef id (data-rask-ref), same selector the ref reviver uses.
    var el = spec.el ? document.querySelector('[data-rask-ref="' + spec.el + '"]') : undefined;
    var result;
    try { result = run(spec.arg, el); } catch (err) { if (spec.rid != null) { raskPostGestureResult(spec.rid, null); } return; }
    var thenable = result && typeof result.then === "function";
    if (spec.rid != null) {
        // Always post back when a result is expected, so the one-shot server-side handler is consumed
        // (never left dangling) — even if the cap returned a non-thenable (e.g. an unavailable capability).
        if (thenable) {
            result.then(function (value) { raskPostGestureResult(spec.rid, value); },
                function () { raskPostGestureResult(spec.rid, null); });
        } else {
            raskPostGestureResult(spec.rid, result == null ? null : result);
        }
    } else if (thenable && result["catch"]) {
        result["catch"](function () {});
    }
});
