// Rask NATIVE client runtime — ES module, loaded by index.native.html inside a platform WebView.
//
// This is the third Rask client "dialect" alongside rask.js (Server, WebSocket) and rask.wasm.js
// (browser WASM, JSImport). It speaks the SAME frame contract as both — the shared diff codec
// (rask-dom.js), full-HTML morph (rask-morph.js), interop helpers (rask-api.js), extended DOM events
// (rask-events.js) and PWA helpers (rask-pwa.js) are spliced in verbatim at the markers below, so the
// DOM-side behaviour is identical across transports. Only the TRANSPORT differs:
//
//   • send(payload)         → posts JSON to the native host over window.__raskSend (WKScriptMessageHandler
//                             on iOS, a [JavascriptInterface] on Android). The host's NativeLiveSession
//                             turns it into a handler/navigate dispatch.
//   • window.__raskNative.applyRender(json)   ← the host calls this (via EvaluateJavaScript) with each
//                             rendered frame; it drives applyDiff / morph exactly like the WASM client.
//   • window.__raskNative.beginInvokeJS / endDotNetInvoke  ← the host calls these for IJSRuntime interop;
//                             results are posted back through send({type:'jsResult'|'dotNetInvoke'}).
//   • On load we post {type:'ready'} so the host fires its first render only once the client is live.
//
// NOTE (PoC parity): the primary click/input/change/submit handlers below are ported from rask.wasm.js.
// Full parity for the remaining transport-side DOM helpers — rAF input/scroll coalescing, keyboard/drag/
// file events, scoped-CSS FOUC gating and Rask.* scoped-JS invoke gating — is a tracked follow-up: those
// blocks are large and identical to rask.wasm.js and should be lifted into a shared module rather than
// re-copied. See docs/native.md.

let root = null;

// ----- Shared framework interop helpers (__raskEl, __raskApi) — Rask.Core/Resources/rask-api.js -----
// @@RASK_API@@

// ----- Transport-agnostic PWA helpers (__raskPush/__raskNotify/__raskBadge/__raskWakeLock) -----
// @@RASK_PWA@@

// ----- The diff codec: applyDiff(ops, names) + applyFrameInvokes(reply, dispatchOne) — rask-dom.js -----
// @@RASK_DOM@@

// ----- The full-HTML morph: morph(target, fresh) + reviveScript — rask-morph.js -----
// @@RASK_MORPH@@

// ----- Scoped-CSS FOUC gating: CSS_FOUC_GUARD_MS + waitForUnappliedHeadCss (diff path) +
//       preloadNewHeadStylesheets (full-HTML path) — Rask.Core/Resources/rask-scoped.js -----
// @@RASK_SCOPED@@

// The "#fragment" of an intercepted nav-link click is stashed here on click and consumed on the
// matching push reply (scroll to the anchor, else the top). Kept for parity with the other clients.
let _pendingScrollHash = "";

function inRoot(el) {
    // Whether an event target is inside the Rask-managed root (so we don't hijack events on, e.g.,
    // a third-party widget mounted outside it). The native root is the whole document body.
    return !!el && (root ? root.contains(el) : true);
}

// ----- Extended GlobalEventHandlers (mouse/wheel/pointer/touch/clipboard/media/beforeinput) -----
// Needs send(payload) + inRoot(el) in scope (declared above). — Rask.Core/Resources/rask-events.js
// @@RASK_EVENTS@@

// ----- Native transport primitives ------------------------------------------------------------

// Post a client→host message. Two platform bridges are supported so neither races page-script execution:
//   • iOS injects window.__raskSend at document-start (a WKUserScript) → a WKScriptMessageHandler.
//   • Android exposes window.__raskBridge.dispatch synchronously via WebView.addJavascriptInterface.
// Either forwards the JSON string to INativeWebView.OnMessage → NativeAppHost.RouteMessageAsync.
function send(payload) {
    try {
        const s = JSON.stringify(payload);
        if (typeof window.__raskSend === "function") {
            window.__raskSend(s);
        } else if (window.__raskBridge && typeof window.__raskBridge.dispatch === "function") {
            window.__raskBridge.dispatch(s);
        } else {
            console.error("[Rask.Native] no native send bridge (window.__raskSend / window.__raskBridge)");
        }
    } catch (e) {
        console.error("[Rask.Native] send failed", e);
    }
}

// Serialize frame application so a deferred body swap can't be overtaken by the next frame.
let _renderQueue = Promise.resolve();

// Called by the host with each rendered frame (a JSON string — the same {kind:"diff",ops} / {html}
// envelope the WASM client receives as bytes). Exposed on window.__raskNative for EvaluateJavaScript.
function applyRender(json) {
    if (!json) return;
    let reply;
    try {
        reply = (typeof json === "string") ? JSON.parse(json) : json;
    } catch (e) {
        console.error("[Rask.Native] applyRender: malformed payload", e);
        return;
    }
    handle(reply);
}

function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        _renderQueue = _renderQueue.then(() => applyDiffReply(reply), () => applyDiffReply(reply));
        return;
    }
    _renderQueue = _renderQueue.then(() => applyFullReply(reply), () => applyFullReply(reply));
}

function dispatchNativeInvoke(inv) {
    beginInvokeJS(
        String(inv.id),
        inv.identifier,
        typeof inv.argsJson === "string" ? inv.argsJson : null,
        typeof inv.resultType === "number" ? inv.resultType : 0,
        typeof inv.targetInstanceId === "number" ? String(inv.targetInstanceId) : "0");
}

function applyDiffReply(reply) {
    // Morph <head> FIRST so a newly mounted component's scoped <link> is present, then defer the
    // body ops until that stylesheet applies (waitForUnappliedHeadCss) so the swapped body never
    // paints unstyled (FOUC) — the same gating rask.js / rask.wasm.js do. Returns the wait Promise
    // so _renderQueue holds the next frame until the body has committed.
    const applyBody = () => {
        applyDiff(reply.ops, Array.isArray(reply.names) ? reply.names : null);
        applyFrameInvokes(reply, dispatchNativeInvoke);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    if (typeof reply.head === "string") {
        const freshHead = new DOMParser().parseFromString(reply.head, "text/html").head;
        if (freshHead) {
            morph(document.head, freshHead);
            const wait = waitForUnappliedHeadCss();
            if (wait) return wait.then(applyBody);
        }
    }
    return applyBody();
}

function applyFullReply(reply) {
    let freshHtml = null;
    if (typeof reply.html === "string" && reply.html.length > 0) {
        freshHtml = new DOMParser().parseFromString(reply.html, "text/html").documentElement;
    }
    // FOUC guard: preload + await any new scoped stylesheet the incoming document adds so the morph
    // paints the styled body only once its sheet has applied (preloadNewHeadStylesheets). Returns
    // null — commit synchronously at today's timing — when the render mounts no new scoped CSS.
    const applyDom = () => {
        if (freshHtml) {
            morph(document.documentElement, freshHtml);
            root = document.querySelector("[data-rask-root]") || document.body;
        }
        applyFrameInvokes(reply, dispatchNativeInvoke);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    if (freshHtml) {
        const wait = preloadNewHeadStylesheets(freshHtml);
        if (wait) return wait.then(applyDom);
    }
    return applyDom();
}

// ----- Primary event handlers (ported from rask.wasm.js) --------------------------------------

// Click — carries the modifier keys the framework surfaces as MouseModifiers.
document.addEventListener("click", function (e) {
    // Nav-link interception: a Rask <a data-rask-nav> click navigates in-app rather than loading a URL.
    const link = e.target && e.target.closest ? e.target.closest("a[data-rask-nav]") : null;
    if (link && inRoot(link)) {
        e.preventDefault();
        const url = new URL(link.href, document.baseURI);
        _pendingScrollHash = url.hash || "";
        if (typeof flushInputsNow === "function") flushInputsNow();
        send({ type: "navigate", path: url.pathname, query: url.search, replace: false });
        return;
    }
    const el = e.target && e.target.closest ? e.target.closest("[data-rask-on-click]") : null;
    if (!el || !inRoot(el)) return;
    if (typeof flushInputsNow === "function") flushInputsNow();
    send({
        id: el.getAttribute("data-rask-on-click"), type: "click",
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    });
});

// Input & scroll — rAF-coalesced dispatch shared with rask.js / rask.wasm.js (rask-input.js). This
// provides the `input` + `scroll` listeners and flushInputsNow(); the change/submit/click handlers
// flush through it so the host processes a pending coalesced input before the dependent action.
// flushInputsNow is a hoisted function, so the keyboard handler spliced above (@@RASK_EVENTS@@) can
// call it regardless of splice order.
// @@RASK_INPUT@@

// Change — report the control's value (checkbox → checked). Flush any pending coalesced input first
// so a change-triggered validator reads the freshly-typed value, not the pre-flush one.
function valueOf(el) {
    if (el.type === "checkbox") return el.checked ? "true" : "false";
    return el.value == null ? "" : String(el.value);
}
document.addEventListener("change", function (e) {
    const el = e.target;
    if (!el || !el.getAttribute) return;
    const id = el.getAttribute("data-rask-on-change");
    if (!id || !inRoot(el)) return;
    if (typeof flushInputsNow === "function") flushInputsNow();
    send({ id: id, type: "change", value: valueOf(el) });
});

// Submit — serialize the form fields into a flat { name: value } bag.
document.addEventListener("submit", function (e) {
    const form = e.target;
    if (!form || !form.getAttribute) return;
    const id = form.getAttribute("data-rask-on-submit");
    if (!id || !inRoot(form)) return;
    e.preventDefault();
    if (typeof flushInputsNow === "function") flushInputsNow();
    const data = {};
    const fd = new FormData(form);
    fd.forEach(function (v, k) { if (typeof v === "string") data[k] = v; });
    send({ id: id, type: "submit", form: data });
});

// ----- IJSRuntime interop (host → JS), ported from rask.wasm.js ---------------------------------

const jsObjectRefs = new Map();
let nextJsObjectRefId = 1;

function jsResolveIdentifier(target, identifier) {
    if (typeof identifier !== "string" || identifier.length === 0) return null;
    const parts = identifier.split(".");
    let parent = target;
    for (let i = 0; i < parts.length - 1; i++) {
        if (parent == null) return null;
        parent = parent[parts[i]];
    }
    if (parent == null) return null;
    return [parent, parts[parts.length - 1]];
}

function jsReviver(_key, value) {
    if (value && typeof value === "object") {
        if (typeof value.__jsObjectId === "number") return jsObjectRefs.get(value.__jsObjectId);
        if (typeof value.__raskRef__ === "string") {
            return document.querySelector(`[data-rask-ref="${CSS.escape(value.__raskRef__)}"]`);
        }
    }
    return value;
}

// Host calls this via EvaluateJavaScript (NativeJSRuntime.DispatchOutsideRender) and the frame-invoke
// path (dispatchNativeInvoke). Runs the identified function and posts the result back as a jsResult.
function beginInvokeJS(taskId, identifier, argsJson, resultType, targetInstanceId) {
    Promise.resolve().then(() => {
        const args = JSON.parse(argsJson || "[]", jsReviver);
        let target = window;
        const targetId = Number(targetInstanceId);
        if (targetId !== 0) {
            target = jsObjectRefs.get(targetId);
            if (!target) throw new Error("Unknown JS object reference: " + targetInstanceId);
        }
        const resolved = jsResolveIdentifier(target, identifier);
        if (!resolved) throw new Error("Could not find '" + identifier + "' on target");
        const fn = resolved[0][resolved[1]];
        return (typeof fn === "function") ? fn.apply(resolved[0], args) : fn;
    }).then((value) => {
        if (resultType === 3) { postJsResult(taskId, true, null); return; }        // void
        if (resultType === 1) {                                                     // JS object ref
            const refId = nextJsObjectRefId++;
            jsObjectRefs.set(refId, value);
            postJsResult(taskId, true, { "__jsObjectId": refId });
            return;
        }
        postJsResult(taskId, true, value === undefined ? null : value);
    }).catch((err) => postJsResult(taskId, false, null, (err && err.message) || String(err)));
}

function postJsResult(taskId, success, result, error) {
    send(success
        ? { type: "jsResult", id: Number(taskId), success: true, result: result }
        : { type: "jsResult", id: Number(taskId), success: false, error: error || "JS invocation failed" });
}

// ----- DotNet shim (window.DotNet, for JS-initiated [JSInvokable]) ------------------------------
const dotNetPending = new Map();
let nextDotNetCallId = 1;

window.DotNet = window.DotNet || {
    invokeMethodAsync(assemblyName, methodIdentifier, ...args) {
        const callId = String(nextDotNetCallId++);
        return new Promise((resolve, reject) => {
            dotNetPending.set(callId, { resolve, reject });
            send({
                type: "dotNetInvoke", callId: callId, assemblyName: assemblyName,
                methodIdentifier: methodIdentifier, dotNetObjectId: 0, argsJson: JSON.stringify(args)
            });
        });
    }
};

// Host calls this via EvaluateJavaScript (NativeJSRuntime.EndInvokeDotNet) to resolve a [JSInvokable].
function endDotNetInvoke(resultJson) {
    let msg;
    try { msg = JSON.parse(resultJson); } catch (e) { console.error("[Rask.Native] endDotNetInvoke bad JSON", e); return; }
    const pending = dotNetPending.get(msg.callId);
    if (!pending) return;
    dotNetPending.delete(msg.callId);
    if (msg.success) pending.resolve(msg.result);
    else pending.reject(new Error(msg.error || "DotNet invocation failed"));
}

// The host reaches these through EvaluateJavaScript.
window.__raskNative = { applyRender, beginInvokeJS, endDotNetInvoke };

// Signal readiness so the host fires its first render only now (see NativeAppHost.RouteMessageAsync).
root = document.querySelector("[data-rask-root]") || document.body;
send({ type: "ready" });
