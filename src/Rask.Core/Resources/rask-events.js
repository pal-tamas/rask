// rask-events.js — the extended GlobalEventHandlers delegation, shared by both client runtimes.
//
// Spliced into the Server runtime (rask.js, at "// @@RASK_EVENTS@@") and the WASM runtime
// (rask.wasm.js) so the two clients can never drift. It relies only on three symbols that both hosts
// define in the surrounding scope: `send(payload)`, `inRoot(el)` and the global `document`.
//
// Model: one capture-phase document listener per event routes to the nearest ancestor carrying
// `data-rask-on-<event>`, then ships a per-category JSON payload tagged with that element's handler id.
// Capture phase is used so non-bubbling events (focus/blur) still reach the delegated listener. Click,
// scroll, keydown/keyup, the four core drag events, input/change/submit keep their own dedicated
// listeners in each host — this file covers everything else (mouse, pointer, touch, wheel, focus,
// clipboard, the remaining drag/form events, and the HTMLMediaElement events). Kept ES5 (var/function)
// because it is spliced verbatim into both hosts. Written defensively: every builder tolerates a
// partial event object.

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
