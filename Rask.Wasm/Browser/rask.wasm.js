// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

let dotnetExports = null;
let root = null;
let lastCssHash = null;
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
    const bytes = decodeBase64(download.bytes);
    if (!bytes) return;
    const blob = new Blob([bytes], {type: download.contentType || "application/octet-stream"});
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = download.filename;
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
        try { document.body.removeChild(a); } catch (_) {}
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

// reviveScript() + morph() are concatenated in at build time from
// Rask.Core/Resources/rask-morph.js by the _RaskBuildClientJs target.
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
        }, { once: true });
    }
    s.text = node.textContent;
    return s;
}

function morph(from, to) {
    if (from.nodeType !== to.nodeType || from.nodeName !== to.nodeName) {
        from.parentNode.replaceChild(to, from);
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
                from.insertBefore(reviveScript(dst), anchor);
            } else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) {
                from.insertBefore(reviveScript(dst), anchor);
                from.removeChild(src);
            } else {
                if (src !== anchor) from.insertBefore(src, anchor);
                else anchor = anchor.nextSibling;
                morph(src, dst);
            }
        }
        // Drop any from-side keyed nodes that were not claimed by the new tree.
        keyMap.forEach(function (n) { if (n.parentNode === from) from.removeChild(n); });
        // Drop trailing unkeyed nodes too.
        while (unkeyedCursor < unkeyedFrom.length) {
            var leftover = unkeyedFrom[unkeyedCursor++];
            if (leftover.parentNode === from) from.removeChild(leftover);
        }
        return;
    }

    var max = Math.max(fc.length, tc.length);
    for (var k = 0; k < max; k++) {
        var src = fc[k], dst = tc[k];
        if (!src) from.appendChild(reviveScript(dst));
        else if (!dst) from.removeChild(src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) from.replaceChild(reviveScript(dst), src);
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
    const applyDom = () => {
        if (freshHtml) {
            morph(document.documentElement, freshHtml);
            root = document.querySelector("[data-rask-root]") || document.body;
        }
        applyHistory(reply.history);
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
    if (typeof reply.cssHash === "string" || reply.cssHash === null)
        applyScopedCss(reply.cssHash, reply.cssText);
    if (reply.download) triggerDownload(reply.download);
}

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
        const reply = await dotnetExports.Rask.Wasm.JSInterop.Dispatch(JSON.stringify(payload));
        console.log("[Rask] send reply bytes:", reply ? reply.length : 0);
        if (typeof reply === "string" && reply.length > 0) {
            try {
                handle(JSON.parse(reply));
            } catch (e) {
                console.error("Rask: malformed dispatch reply", e, reply);
            }
        }
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
    send({id: t.getAttribute("data-rask-on-click"), type: "click",
          shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey});
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
    if (inputRaf) { cancelAnimationFrame(inputRaf); inputRaf = 0; }
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
