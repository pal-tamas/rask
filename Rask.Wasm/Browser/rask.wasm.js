// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

let dotnetExports = null;
let root = null;
let lastCssHash = null;
let lastJsHash = null;
let basePath = null;

// Read once from <base href> (or the page URL if no <base> is set) so the
// runtime can host under a sub-path like /Rask/ on GitHub Pages without the
// .NET side ever seeing the prefix. Resolves to the directory portion so a
// page URL like /index.html yields "/" (not "/index.html/").
function getBasePath() {
    if (basePath !== null) return basePath;
    const p = new URL(document.baseURI).pathname;
    const last = p.lastIndexOf("/");
    basePath = last < 0 ? "/" : p.slice(0, last + 1);
    return basePath;
}

function stripBase(pathname) {
    const b = getBasePath();
    if (b === "/" || !pathname) return pathname;
    if (pathname === b.slice(0, -1) || pathname === b) return "/";
    return pathname.startsWith(b) ? "/" + pathname.slice(b.length) : pathname;
}

function prependBase(url) {
    const b = getBasePath();
    if (b === "/" || typeof url !== "string" || !url.startsWith("/") || url.startsWith(b)) return url;
    return b + url.slice(1);
}

// Called from main.js once `getAssemblyExports` is available so the JS event
// handlers below can dispatch into .NET via the JSExport surface.
export function setExports(exports) {
    dotnetExports = exports;
    root = document.querySelector("[data-rask-root]") || document.body;
    const ok = !!(exports && exports.Rask && exports.Rask.Wasm
        && exports.Rask.Wasm.JSInterop && typeof exports.Rask.Wasm.JSInterop.Dispatch === "function");
    console.log("[Rask] setExports — Dispatch reachable:", ok, "root:", root && root.tagName);
    // Install the InvokeJsAsync result-shipper. WASM routes results back through
    // the ResolveJsInvoke JSExport — .NET-side calls JsInvokeResultStore.TryResolve
    // to complete the awaiting Task<T>.
    if (window.Rask && window.Rask.scoped && exports && exports.Rask
        && exports.Rask.Wasm && exports.Rask.Wasm.JSInterop
        && typeof exports.Rask.Wasm.JSInterop.ResolveJsInvoke === "function") {
        window.Rask.scoped._sendResult = function (id, value, error) {
            let payload;
            if (value === null || value === undefined) payload = null;
            else if (typeof value === "string") payload = value;
            else payload = JSON.stringify(value);
            exports.Rask.Wasm.JSInterop.ResolveJsInvoke(id, payload, error || null);
        };
    }
    // The framework no longer auto-fires scoped-JS `rendered` hooks. C# user code
    // invokes them via `InvokeJs(method, args)` from a C# lifecycle hook (typically
    // OnRendered); the resulting `scopedJsInvokes` payload field is dispatched
    // inside handle() after each morph.
}

// Called by .NET (via [JSImport]) for both the initial paint and subsequent
// background re-renders. `payload` is a Uint8Array carrying the UTF-8 JSON
// frame the C# side built via LivePayload.BuildPayloadUtf8WithRoot — same
// shape as the WS frame the server emits. One TextDecoder pass + JSON.parse
// replaces the previous 5-string marshal across the JS boundary.
const _payloadDecoder = new TextDecoder("utf-8");

export function applyRender(payload) {
    if (!payload || payload.length === 0) return;
    let reply;
    try {
        reply = JSON.parse(_payloadDecoder.decode(payload));
    } catch (e) {
        console.error("[Rask] applyRender: malformed payload", e);
        return;
    }
    handle(reply);
}

// File registry for input[type=file]: maps short refs -> live File objects.
// Cleared when the file input fires another change so old refs become unreachable.
const fileRegistry = new Map();

export async function readFileChunk(ref, offset, length) {
    const file = fileRegistry.get(ref);
    if (!file) return new Uint8Array();
    const end = Math.min(file.size, offset + length);
    const slice = file.slice(offset, end);
    const buf = await slice.arrayBuffer();
    return new Uint8Array(buf);
}

function registerFiles(inputEl, files) {
    // Drop any prior refs for this input so a re-pick doesn't pile up File objects.
    if (inputEl.__raskFileRefs) {
        for (const r of inputEl.__raskFileRefs) fileRegistry.delete(r);
    }
    const metas = [];
    const refs = [];
    for (const f of files) {
        const r = (crypto && crypto.randomUUID) ? crypto.randomUUID() : "f-" + Math.random().toString(36).slice(2);
        fileRegistry.set(r, f);
        refs.push(r);
        metas.push({
            ref: r,
            name: f.name,
            size: f.size,
            type: f.type || "application/octet-stream",
            lastModified: f.lastModified || 0
        });
    }
    inputEl.__raskFileRefs = refs;
    return metas;
}

function triggerDownload(download) {
    if (!download || typeof download.filename !== "string") return;
    let bytes;
    if (typeof download.token === "string" && download.token.length > 0
        && dotnetExports && dotnetExports.Rask && dotnetExports.Rask.Wasm
        && dotnetExports.Rask.Wasm.JSInterop
        && typeof dotnetExports.Rask.Wasm.JSInterop.PullDownload === "function") {
        // Token-pull path: bytes live in .NET, JSExport returns them directly as a Uint8Array.
        // No base64 inflation, no decode loop — render payload only carried the token string.
        bytes = dotnetExports.Rask.Wasm.JSInterop.PullDownload(download.token);
    } else if (typeof download.bytes === "string") {
        // Legacy base64-inline path (test seam + back-compat).
        bytes = decodeBase64(download.bytes);
    }
    if (!bytes || bytes.length === 0) return;
    const blob = new Blob([bytes], {type: download.contentType || "application/octet-stream"});
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = download.filename;
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
        try {
            document.body.removeChild(a);
        } catch (_) {
        }
        URL.revokeObjectURL(url);
    }, 0);
}

function decodeBase64(b64) {
    if (typeof b64 !== "string" || b64.length === 0) return null;
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
}

export function getLocation() {
    return stripBase(location.pathname) + location.search;
}

export function getBaseAddress() {
    return new URL(document.baseURI).href;
}

export function pushHistory(url, replace) {
    const target = prependBase(url);
    if (replace) window.history.replaceState({rask: true}, "", target);
    else window.history.pushState({rask: true}, "", target);
}

function inRoot(el) {
    return root && root.contains(el);
}

function applyScopedCss(hash, cssText) {
    if (hash === lastCssHash && cssText == null) return;
    lastCssHash = hash;
    let style = document.getElementById("rask-scoped");
    if (!style) {
        style = document.createElement("style");
        style.id = "rask-scoped";
        // Marker so morph() leaves this element alone — it's owned by JS, not
        // by the .NET render tree, and would otherwise get trimmed off when
        // the head's last child slot doesn't match between renders.
        style.setAttribute("data-rask-managed", "");
        document.head.appendChild(style);
    }
    if (typeof cssText === "string") style.textContent = cssText;
}

function applyScopedJs(hash, jsText) {
    if (hash === lastJsHash && jsText == null) return;
    if (typeof jsText !== "string" || jsText.length === 0) return;
    lastJsHash = hash;
    // Replace the script element rather than mutate textContent — re-assigning a
    // script's textContent does NOT re-execute it. A fresh <script> with the new
    // body runs the new Rask.scoped.register(...) calls, then we walkUnmount +
    // walkMount to refresh live elements' hooks (hot-reload story).
    const existing = document.getElementById("rask-scoped-js");
    if (existing && existing.parentNode) existing.parentNode.removeChild(existing);
    const script = document.createElement("script");
    script.id = "rask-scoped-js";
    script.setAttribute("data-rask-managed", "");
    script.textContent = jsText;
    document.head.appendChild(script);
    // Bundle just (re-)evaluated — register() calls have populated the registry.
    // C#-queued invocations that arrived before this point hit an empty registry
    // and no-op'd; re-invoke "rendered" against every registered scope with
    // firstRender=true. Modules without a "rendered" export silently no-op.
    if (!window.Rask || !window.Rask.scoped) return;
    const marked = document.querySelectorAll("[data-rask-mount]");
    const seen = new Set();
    for (const node of marked) {
        const s = node.getAttribute("data-rask-mount");
        if (s && !seen.has(s)) {
            seen.add(s);
            window.Rask.scoped.invoke(s, "rendered", null, [true]);
        }
    }
}

// The Rask.scoped dispatcher + reviveScript() + morph() are concatenated in at
// build time by the _RaskSpliceClientJs target. Order matters: the dispatcher
// must be defined before morph() references it.
// Scoped-JS dispatcher. Concatenated into both rask.js (Server) and rask.wasm.js (WASM)
// at build time via the @@RASK_SCOPED@@ marker — see _RaskBuildClientJs / _RaskSpliceClientJs
// in Rask.Server.csproj and Rask.Wasm.csproj.
//
// Public author surface (sibling `.js` next to a Component):
//   export function rendered(el, firstRender) { /* ... */ }
//   export async function fetchSomething(el, key) { return await x.fetch(key); }
//   // any number of named exports — each becomes a method on the scoped module.
//
// Invocation model: NOT automatic. C# user code calls
//   InvokeJs("name", ...args)             — fire-and-forget
//   InvokeJsAsync<T>("name", ...args)     — await the return value
// from a lifecycle hook (typically OnRendered). The framework ships queued
// invocations in the render payload as `scopedJsInvokes`; the client runtime calls
//   Rask.scoped.invoke(scopeId, method, idOrNull, args)
// for each entry after morph completes. The dispatcher looks up `method` on the
// registered module object, calls it as `module[method](el, ...args)` for the
// first matching `data-rask-mount` element, awaits any returned Promise, and
// — when `idOrNull` is a number — ships the result back via the host-installed
// `Rask.scoped._sendResult(id, value, error)` bridge.
window.Rask = window.Rask || {};
Rask.scoped = (function () {
    var registry = new Map(); // scopeId -> { name: function, ... }

    function register(scopeId, factory) {
        var methods;
        try {
            methods = (typeof factory === 'function') ? factory() : factory;
        } catch (e) {
            console.error('[Rask] scoped-js factory failed for ' + scopeId, e);
            return;
        }
        if (methods && typeof methods === 'object') {
            registry.set(scopeId, methods);
        }
    }

    // host runtime installs this hook to ship the result back across the
    // appropriate transport (WS message on server, JSExport call on WASM).
    function _sendResult(id, value, error) {
        // default no-op — overridden by rask.js / rask.wasm.js
    }

    function _serializeResult(value) {
        if (value === undefined) return null;
        // Keep the wire payload narrow: primitives travel as JSON-native; everything
        // else (objects, arrays, classes) stringifies. C# DeserializeResult<T>
        // handles primitives directly and falls back to the JSON raw text for string T.
        var t = typeof value;
        if (t === 'boolean' || t === 'number' || t === 'string' || value === null) return value;
        try {
            return JSON.stringify(value);
        } catch (e) {
            return String(value);
        }
    }

    function invoke(scopeId, method, id, args) {
        if (!scopeId || !method) return;
        var hasId = (typeof id === 'number');
        var methods = registry.get(scopeId);
        if (!methods) {
            if (hasId) _sendResult(id, null, null);
            return;
        }
        var fn = methods[method];
        if (typeof fn !== 'function') {
            if (hasId) _sendResult(id, null, null);
            return;
        }
        var extra = Array.isArray(args) ? args : [];
        // For fire-and-forget invocations, dispatch against EVERY matching element.
        // For await-the-result invocations (id present), only the first matching
        // element's return value is shipped back — matches Component.InvokeJsAsync's
        // documented contract.
        var nodes = document.querySelectorAll('[data-rask-mount="' + scopeId + '"]');
        if (hasId) {
            var node = nodes[0];
            if (!node) {
                _sendResult(id, null, null);
                return;
            }
            try {
                var result = fn.apply(null, [node].concat(extra));
                if (result && typeof result.then === 'function') {
                    result.then(
                        function (v) {
                            _sendResult(id, _serializeResult(v), null);
                        },
                        function (err) {
                            _sendResult(id, null, (err && err.message) || String(err));
                        }
                    );
                } else {
                    _sendResult(id, _serializeResult(result), null);
                }
            } catch (e) {
                _sendResult(id, null, e && e.message || String(e));
            }
            return;
        }
        for (var i = 0; i < nodes.length; i++) {
            var n = nodes[i];
            try {
                fn.apply(null, [n].concat(extra));
            } catch (e) {
                console.error('[Rask] ' + method + ' failed for ' + scopeId, e);
            }
        }
    }

    return {
        register: register,
        invoke: invoke,
        // The host runtime patches this with a transport-specific sender.
        set _sendResult(fn) {
            _sendResult = fn;
        },
        get _sendResult() {
            return _sendResult;
        }
    };
})();

// Shared client-side morph algorithm consumed by both rask.js (Server) and
// rask.wasm.js (WASM). Concatenated into each runtime at build time — see the
// MSBuild "_RaskBuildClientJs" target in Rask.Server.csproj and Rask.Wasm.csproj.
//
// Why concat instead of import / network split:
//  - rask.js is a classic <script> served from /rask/rask.js (no ES-module hook).
//  - rask.wasm.js is loaded by JSHost.ImportAsync as an ES module.
// Concat sidesteps the loader mismatch and keeps the single-file delivery model.

// Scripts produced by DOMParser have their "already started" flag set, so the
// browser silently skips them when morph() appends them into the live document.
// Rebuild script nodes via createElement so they actually execute, propagate
// every attribute (type=module, defer, integrity, nonce, crossorigin, …), and
// fire raskAfterMorph again once external scripts finish loading — inline
// scripts run synchronously on insertion and may early-return if they depend
// on a not-yet-loaded global like window.hljs.
function reviveScript(node) {
    if (!node || node.nodeType !== 1 || node.tagName !== "SCRIPT") return node;
    var s = document.createElement("script");
    for (var i = 0; i < node.attributes.length; i++) {
        var a = node.attributes[i];
        s.setAttribute(a.name, a.value);
    }
    if (s.src) {
        s.async = false;
        s.addEventListener("load", function () {
            if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
        }, {once: true});
    }
    s.text = node.textContent;
    return s;
}

// Wrappers around the underlying DOM mutation primitives. Scoped-JS hooks are
// NOT auto-fired by morph any more — C# code drives invocations explicitly via
// InvokeJs(name, ...args) from a lifecycle hook (typically OnRendered); the
// resulting `scopedJsInvokes` payload field is dispatched by the runtime after
// morph completes. If a user needs teardown on element removal, they should use
// a MutationObserver inside their hook or expose an explicit "removed" method
// and call it from OnUnmount via InvokeJs.
function _raskInsertBefore(parent, dst, anchor) {
    parent.insertBefore(dst, anchor);
}

function _raskAppendChild(parent, dst) {
    parent.appendChild(dst);
}

function _raskRemoveChild(parent, src) {
    parent.removeChild(src);
}

function _raskReplaceChild(parent, dst, src) {
    parent.replaceChild(dst, src);
}

function morph(from, to) {
    if (from.nodeType !== to.nodeType || from.nodeName !== to.nodeName) {
        _raskReplaceChild(from.parentNode, to, from);
        return;
    }
    if (from.nodeType === 3 || from.nodeType === 8) {
        if (from.nodeValue !== to.nodeValue) from.nodeValue = to.nodeValue;
        return;
    }
    var fa = from.attributes, ta = to.attributes;
    for (var i = fa.length - 1; i >= 0; i--) {
        var name = fa[i].name;
        if (!to.hasAttribute(name)) from.removeAttribute(name);
    }
    for (var j = 0; j < ta.length; j++) {
        var a = ta[j];
        if (from.getAttribute(a.name) !== a.value) from.setAttribute(a.name, a.value);
    }
    var tag = from.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA") {
        // Only inputs with data-rask-on-input stream keystrokes — those need the
        // focus guard so a lagging re-render doesn't clobber mid-typed characters.
        // Change-only inputs (date / number / time / datetime-local / checkbox /
        // radio) commit at change time; the rendered value is canonical and must
        // win, otherwise Chromium leaves a focused date input's dirty value flag
        // stale and the first picker change appears to be dropped.
        var streaming = from.hasAttribute("data-rask-on-input") || to.hasAttribute("data-rask-on-input");
        if (!streaming || document.activeElement !== from) {
            var newVal = to.getAttribute("value");
            if (newVal === null && to.tagName === "TEXTAREA") newVal = to.textContent;
            if (newVal === null) newVal = "";
            if (from.value !== newVal) from.value = newVal;
            var checked = to.hasAttribute("checked");
            if (from.checked !== checked) from.checked = checked;
        }
    }
    // Skip JS-owned elements (marked data-rask-managed) — they're not part of
    // the .NET render tree, so pairing them against the incoming children would
    // either trim them off or replace them with something unrelated. The server
    // runtime emits no such nodes today; the filter is a no-op there.
    var fc = [], tc = [];
    for (var n = from.firstChild; n; n = n.nextSibling) {
        if (n.nodeType === 1 && n.hasAttribute("data-rask-managed")) continue;
        fc.push(n);
    }
    for (var m = to.firstChild; m; m = m.nextSibling) tc.push(m);

    // Keyed reconciliation: if any incoming child carries data-rask-key, match
    // by key instead of by position so reordered list items keep their DOM
    // identity (focus, scroll, animations, ::part state) across re-renders.
    // Falls back to the positional walk below when no keys are present.
    var keyed = false;
    for (var ki = 0; ki < tc.length; ki++) {
        if (tc[ki].nodeType === 1 && tc[ki].getAttribute && tc[ki].getAttribute("data-rask-key") !== null) {
            keyed = true;
            break;
        }
    }
    if (keyed) {
        var keyMap = new Map();
        var unkeyedFrom = [];
        for (var fi = 0; fi < fc.length; fi++) {
            var fn = fc[fi];
            var fk = (fn.nodeType === 1 && fn.getAttribute) ? fn.getAttribute("data-rask-key") : null;
            if (fk !== null) keyMap.set(fk, fn);
            else unkeyedFrom.push(fn);
        }
        var unkeyedCursor = 0;
        // Sentinel: keep the place we want to insert before. As we move/create
        // keyed nodes we advance this past the just-placed node; unkeyed nodes
        // follow the same anchor.
        var anchor = (fc.length > 0) ? fc[0] : null;
        for (var ti = 0; ti < tc.length; ti++) {
            var dst = tc[ti];
            var dk = (dst.nodeType === 1 && dst.getAttribute) ? dst.getAttribute("data-rask-key") : null;
            var src;
            if (dk !== null) {
                src = keyMap.get(dk) || null;
                if (src) keyMap.delete(dk);
            } else {
                src = unkeyedFrom[unkeyedCursor++] || null;
            }
            if (src === null) {
                _raskInsertBefore(from, reviveScript(dst), anchor);
            } else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) {
                _raskInsertBefore(from, reviveScript(dst), anchor);
                _raskRemoveChild(from, src);
            } else {
                if (src !== anchor) from.insertBefore(src, anchor);
                else anchor = anchor.nextSibling;
                morph(src, dst);
            }
        }
        // Drop any from-side keyed nodes that were not claimed by the new tree.
        keyMap.forEach(function (n) {
            if (n.parentNode === from) _raskRemoveChild(from, n);
        });
        // Drop trailing unkeyed nodes too.
        while (unkeyedCursor < unkeyedFrom.length) {
            var leftover = unkeyedFrom[unkeyedCursor++];
            if (leftover.parentNode === from) _raskRemoveChild(from, leftover);
        }
        return;
    }

    var max = Math.max(fc.length, tc.length);
    for (var k = 0; k < max; k++) {
        var src = fc[k], dst = tc[k];
        if (!src) _raskAppendChild(from, reviveScript(dst));
        else if (!dst) _raskRemoveChild(from, src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) _raskReplaceChild(from, reviveScript(dst), src);
        else morph(src, dst);
    }
}


function applyHistory(history) {
    if (!history || typeof history.url !== "string") return;
    const target = prependBase(history.url);
    if (history.action === "replace")
        window.history.replaceState({rask: true}, "", target);
    else
        window.history.pushState({rask: true}, "", target);
}

function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    let freshHtml = null;
    if (typeof reply.html === "string" && reply.html.length > 0) {
        const doc = new DOMParser().parseFromString(reply.html, "text/html");
        // Morph the whole <html> element so head changes (title, stylesheet links,
        // scoped-css link) propagate too — the App component owns the full page,
        // not just <body>. The bootstrap <script src="main.js"> in the original
        // index.html may get removed by morph if the App's body doesn't include
        // an equivalent; that's harmless because the module is already running.
        freshHtml = doc.documentElement;
    }
    // All post-morph work (history push, scoped CSS/JS apply, scoped-JS dispatch,
    // raskAfterMorph hook) runs INSIDE the applyDom callback. document.startViewTransition
    // schedules the callback so the DOM mutations are batched with snapshot capture —
    // code sitting outside it would run on the OLD DOM and dispatch against stale
    // data-rask-mount elements (or none on a from-empty navigation). Bundling
    // everything inside the same callback guarantees morph completes before dispatch
    // reads the DOM, with or without view transitions.
    const applyDom = () => {
        if (freshHtml) {
            morph(document.documentElement, freshHtml);
            root = document.querySelector("[data-rask-root]") || document.body;
        }
        applyHistory(reply.history);
        if (typeof reply.cssHash === "string" || reply.cssHash === null)
            applyScopedCss(reply.cssHash, reply.cssText);
        if (typeof reply.jsHash === "string" || reply.jsHash === null)
            applyScopedJs(reply.jsHash, reply.jsText);
        // Scoped-JS `rendered` hooks are NOT auto-fired — dispatched here based on
        // the `scopedJsInvokes` payload field. Each entry calls
        // `methods[inv.method](el, ...inv.args)`; when `id` is present the
        // dispatcher ships the result back through the _sendResult bridge to
        // complete the awaiting Task<T>.
        if (Array.isArray(reply.scopedJsInvokes) && window.Rask && window.Rask.scoped) {
            for (const inv of reply.scopedJsInvokes) {
                if (inv && typeof inv.scope === "string" && typeof inv.method === "string") {
                    const args = Array.isArray(inv.args) ? inv.args : [];
                    const invId = (typeof inv.id === "number") ? inv.id : null;
                    window.Rask.scoped.invoke(inv.scope, inv.method, invId, args);
                }
            }
        }
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    // Animate navigations (renders carrying a history block) with the View
    // Transitions API when the browser supports it. State-only re-renders skip
    // the wrap so event-handler latency stays tight.
    if (reply.history && typeof document.startViewTransition === "function") {
        document.startViewTransition(applyDom);
    } else {
        applyDom();
    }
    if (reply.download) triggerDownload(reply.download);
}

// Cached at module scope: TextEncoder construction is cheap but not free, and a
// steady-typing user fires `send` ~60×/sec via the rAF input-coalescing path.
const _sendEncoder = new TextEncoder();

async function send(payload) {
    console.log("[Rask] send", payload);
    if (!dotnetExports) {
        console.warn("[Rask] send: dotnetExports not set");
        return;
    }
    if (!dotnetExports.Rask || !dotnetExports.Rask.Wasm || !dotnetExports.Rask.Wasm.JSInterop) {
        console.error("[Rask] send: Dispatch path missing on exports", dotnetExports);
        return;
    }
    try {
        // Dispatch now marshals the request as a byte[] (cuts the per-event UTF-16 string
        // copy across the JS/.NET boundary that the prior string signature forced) and
        // .NET pushes the response back through the existing applyRender JSImport — the
        // JSExport generator doesn't support Task<byte[]> return types. JS just awaits
        // completion; the morph happens via the applyRender callback path.
        const requestBytes = _sendEncoder.encode(JSON.stringify(payload));
        await dotnetExports.Rask.Wasm.JSInterop.Dispatch(requestBytes);
    } catch (e) {
        console.error("Rask: dispatch failed", e);
    }
}

document.addEventListener("click", (e) => {
    if (e.defaultPrevented) return;
    if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target.closest("a[data-rask-nav]");
    if (!a) return;
    console.log("[Rask] navlink click", a.getAttribute("href"));
    if (a.getAttribute("target") === "_blank") return;
    const href = a.getAttribute("href");
    if (!href) return;
    let url;
    try {
        url = new URL(href, location.href);
    } catch (_) {
        return;
    }
    if (url.origin !== location.origin) return;
    e.preventDefault();
    flushInputsNow();
    send({type: "navigate", path: stripBase(url.pathname), query: url.search});
});

window.addEventListener("popstate", () => {
    flushInputsNow();
    send({type: "navigate", path: stripBase(location.pathname), query: location.search, replace: true});
});

document.addEventListener("click", (e) => {
    const t = e.target.closest("[data-rask-on-click]");
    if (!t || !inRoot(t)) return;
    e.preventDefault();
    flushInputsNow();
    send({
        id: t.getAttribute("data-rask-on-click"), type: "click",
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    });
});

// Input events fire per keystroke — on fast typing that's 5–10 messages over the
// JS interop / WS boundary per second per input. Coalesce per-element with rAF:
// the same element typed into multiple times within one frame produces a single
// outgoing message carrying the latest value at flush time. The element itself
// is the de-duping key — multiple inputs in the same frame each get one message.
// flushInputsNow() is called at the top of every other event handler (change,
// submit, click, navigate) so the server always processes input events before
// the subsequent action that depends on them — without this, a change event
// triggered immediately after typing reaches the server BEFORE the coalesced
// input, and any validator the change kicks off reads the stale model value.
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

document.addEventListener("change", (e) => {
    const t = e.target.closest("[data-rask-on-change], [data-rask-on-files]");
    if (!t || !inRoot(t)) return;
    // Flush before processing — if the same element (or a sibling) has a pending
    // coalesced input, the server needs to see it BEFORE the change-triggered
    // validator / handler runs, otherwise the validator reads stale model state.
    flushInputsNow();
    if (t.tagName === "INPUT" && t.type === "file" && t.hasAttribute("data-rask-on-files")) {
        const files = t.files;
        if (!files || files.length === 0) return;
        const metas = registerFiles(t, files);
        send({id: t.getAttribute("data-rask-on-files"), type: "files", files: metas});
        return;
    }
    if (t.hasAttribute("data-rask-on-change")) {
        send({id: t.getAttribute("data-rask-on-change"), type: "change", value: t.value});
    }
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

document.addEventListener("submit", (e) => {
    const t = e.target.closest("[data-rask-on-submit]");
    if (!t || !inRoot(t)) return;
    e.preventDefault();
    flushInputsNow();
    const fileInputs = t.querySelectorAll('input[type="file"][name]');
    const fileFields = {};
    for (const input of fileInputs) {
        if (!input.files || input.files.length === 0) continue;
        fileFields[input.name] = registerFiles(input, input.files);
    }
    const fd = new FormData(t);
    const obj = {};
    fd.forEach((v, k) => {
        if (v instanceof File || v instanceof Blob) return;
        obj[k] = String(v);
    });
    if (Object.keys(fileFields).length > 0) obj.__files = fileFields;
    send({id: t.getAttribute("data-rask-on-submit"), type: "submit", form: obj});
});
