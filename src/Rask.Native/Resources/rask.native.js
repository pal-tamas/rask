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

// ----- Dev-only "hot reload applied" indicator — Rask.Core/Resources/rask-hotreload.js -----
// The same source the Server and WASM clients use. Exposes window.__raskHotReloadPill, which the host
// calls over the WebView bridge (NativeLiveSession) rather than pushing a frame. Inert on a device
// build, where hot reload is unsupported and nothing ever calls it — see #565.
// @@RASK_HOTRELOAD@@

// The development error panel (showDevError / hideDevError), shared with the Server runtime.
// @@RASK_DEVERROR@@

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

// Navigator.Download staged bytes on the host. There is nothing useful a WebView can do with them itself —
// <a download> is inert in a WKWebView — so the client's whole job is to hand the token straight back, and
// the host pulls the bytes and gives them to the platform (INativeFileExport → the OS share sheet).
function applyDownload(download) {
    if (!download || typeof download.token !== "string" || download.token.length === 0) return;
    send({ type: "download", token: download.token });
}

function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    // Before the render is queued: the download is an out-of-band side effect with no dependency on the DOM
    // commit, and queueing it behind the morph would delay the share sheet behind a FOUC wait for no reason.
    applyDownload(reply.download);
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

// Reflect the host-authored route change in the WebView's own history/location. There is no visible
// address bar on a device, but the WebView still keeps a history stack — so this is what makes hardware
// Back / forward work (via the popstate listener below) and what drives URL-routed UI (e.g. a dialog
// routed at /todos/new, Navigator.SetQuery). Mirrors applyHistory in rask.js / rask.wasm.js; there's no
// base-path prefix on native (the app is served from the origin root).
function applyHistory(history) {
    if (!history || typeof history.url !== "string") return;
    let target = history.url;
    if (history.action === "replace") {
        window.history.replaceState({ rask: true }, "", target);
    } else {
        if (_pendingScrollHash) target += _pendingScrollHash;
        window.history.pushState({ rask: true }, "", target);
    }
    _pendingScrollHash = "";
}

function applyDiffReply(reply) {
    // Morph <head> FIRST so a newly mounted component's scoped <link> is present, then defer the
    // body ops until that stylesheet applies (waitForUnappliedHeadCss) so the swapped body never
    // paints unstyled (FOUC) — the same gating rask.js / rask.wasm.js do. Returns the wait Promise
    // so _renderQueue holds the next frame until the body has committed.
    const applyBody = () => {
        applyDiff(reply.ops, Array.isArray(reply.names) ? reply.names : null);
        applyHistory(reply.history);
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
        applyHistory(reply.history);
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

// Hardware Back / forward: the WebView pops its own history entry (pushed by applyHistory), so ask the
// host to navigate to the now-current location and re-render it. `replace` so the reply's applyHistory
// re-syncs the entry instead of pushing a duplicate.
window.addEventListener("popstate", function () {
    if (typeof flushInputsNow === "function") flushInputsNow();
    send({ type: "navigate", path: location.pathname, query: location.search, replace: true });
});

// Input & scroll — rAF-coalesced dispatch shared with rask.js / rask.wasm.js (rask-input.js). This
// provides the `input` + `scroll` listeners and flushInputsNow(); the change/submit/click handlers
// flush through it so the host processes a pending coalesced input before the dependent action.
// flushInputsNow is a hoisted function, so the keyboard handler spliced above (@@RASK_EVENTS@@) can
// call it regardless of splice order.
// @@RASK_INPUT@@

// ----- input[type=file] ref registry (raskRegisterFiles / raskReadFileChunkBase64) — rask-files.js -----
// Shared with the WASM client. The host reads the bytes back through window.__raskFiles.readChunkBase64
// over IJSRuntime — see NativeFileBackend.
// @@RASK_FILES@@

// Change — report the control's value (checkbox → checked), or the picked files for a file input. Flush any
// pending coalesced input first so a change-triggered validator reads the freshly-typed value, not the
// pre-flush one.
document.addEventListener("change", function (e) {
    const el = e.target;
    if (!el || !el.getAttribute) return;
    if (!inRoot(el)) return;
    // A file input carries data-rask-on-files, not data-rask-on-change: the frame ships file metadata rather
    // than a value, and el.value on a file input is only the browser's fakepath stub.
    // (No backslashes in this template — the MSBuild splice path-normalizes them into forward slashes.)
    if (el.tagName === "INPUT" && el.type === "file" && el.hasAttribute("data-rask-on-files")) {
        const files = el.files;
        if (!files || files.length === 0) return;
        if (typeof flushInputsNow === "function") flushInputsNow();
        send({ id: el.getAttribute("data-rask-on-files"), type: "files", files: raskRegisterFiles(el, files) });
        return;
    }
    const id = el.getAttribute("data-rask-on-change");
    if (!id) return;
    if (typeof flushInputsNow === "function") flushInputsNow();
    // Through the shared module (rask-morph.js, spliced above at @@RASK_MORPH@@) rather than a local
    // valueOf — this host had its own copy, which is exactly the drift that left <select> unguarded.
    // `values` is null for everything except a <select multiple>, whose `.value` is only its FIRST
    // selected option.
    const frame = { id: id, type: "change", value: raskChangeFrameValue(el) };
    const values = raskChangeFrameValues(el);
    if (values !== null) frame.values = values;
    send(frame);
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
    // File inputs inside the form: FormData yields File objects, which the loop above drops (they are not
    // strings). Register them the same way the change path does and carry the metadata under __files, which
    // is the key Core's FormData reader looks under. Same shape as the WASM client.
    const fileFields = {};
    for (const input of form.querySelectorAll('input[type="file"][name]')) {
        if (!input.files || input.files.length === 0) continue;
        fileFields[input.name] = raskRegisterFiles(input, input.files);
    }
    if (Object.keys(fileFields).length > 0) data.__files = fileFields;
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

// The host reaches applyRender/beginInvokeJS/endDotNetInvoke through EvaluateJavaScript. capabilities +
// invoke() are the native device-capability bridge the shared client uses (e.g. Shareable): invoke() posts
// a capability message the host routes to the backend the head registered — see NativeAppHost.
//
// capabilities is EMPTY here and filled in by the host (NativeCapabilityRegistry), because what this app
// backs natively is a property of the platform module it was given, not of the client. It used to be a
// hardcoded ["share"], which is why share was the only capability that ever worked.
const capabilityPending = new Map();
const capabilitySubs = new Map();
let nextCapabilityId = 1;

window.__raskNative = {
    applyRender, beginInvokeJS, endDotNetInvoke,
    capabilities: [],
    has: function (name) { return window.__raskNative.capabilities.indexOf(name) !== -1; },

    // Returns a promise, resolved by capabilityResult below. The same correlation-id shape jsResult and
    // dotNetInvoke already use in the other direction — without it an invoke could only be fire-and-forget,
    // and every capability that returns a value was unreachable.
    invoke: function (component, op, data) {
        const id = String(nextCapabilityId++);
        return new Promise((resolve, reject) => {
            capabilityPending.set(id, { resolve, reject });
            send({
                type: "capability", id: id, component: component, op: op,
                data: data === undefined || data === null ? null : JSON.stringify(data)
            });
        });
    },

// Streams. The subscription id is minted HERE, not returned by the host: a sensor can deliver a reading
    // before the reply arrives, and an id the page has not seen yet is one it cannot route — the first
    // readings would vanish, silently and only on a fast device. Registering the callback first removes the
    // race entirely.
    subscribe: function (component, op, data, onEvent) {
        const sub = "s" + String(nextCapabilityId++);
        capabilitySubs.set(sub, onEvent);
        const payload = Object.assign({ sub: sub }, data || {});
        return window.__raskNative.invoke(component, op, payload).then(() => sub, (err) => {
            capabilitySubs.delete(sub);
            throw err;
        });
    },

    unsubscribe: function (component, op, sub) {
        capabilitySubs.delete(sub);
        return window.__raskNative.invoke(component, op, sub);
    },

    // Host → page: one reading. Handed to the callback the page registered, which forwards it to C#
    // exactly as the web implementation would — the bridge changes where a reading comes from, not how it
    // gets home.
    capabilityEvent: function (json) {
        let msg;
        try { msg = JSON.parse(json); } catch (e) { return; }
        const cb = capabilitySubs.get(msg.sub);
        if (!cb) return;
        let value = null;
        if (msg.payload !== null && msg.payload !== undefined) {
            try { value = JSON.parse(msg.payload); } catch (e) { value = msg.payload; }
        }
        cb(value);
    },

    // Host → page: settle the promise the invoke above is waiting on.
    capabilityResult: function (json) {
        let msg;
        try { msg = JSON.parse(json); } catch (e) { console.error("[Rask.Native] capabilityResult bad JSON", e); return; }
        const pending = capabilityPending.get(msg.id);
        if (!pending) return;
        capabilityPending.delete(msg.id);
        if (msg.success) {
            let value = null;
            if (msg.result !== null && msg.result !== undefined) {
                try { value = JSON.parse(msg.result); } catch (e) { value = msg.result; }
            }
            pending.resolve(value);
        } else {
            pending.reject(new Error(msg.error || "The native capability failed."));
        }
    }
};

// Signal readiness so the host fires its first render only now (see NativeAppHost.RouteMessageAsync).
root = document.querySelector("[data-rask-root]") || document.body;
send({ type: "ready" });
