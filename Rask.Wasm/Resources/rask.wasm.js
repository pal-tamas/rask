// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

let dotnetExports = null;
let root = null;
let lastCssHash = null;
let lastJsHash = null;
let basePath = null;

// Scoped-JS bundle gate. The bundle script is appended by applyScopedJs() and
// executes synchronously upon insertion — that's the point at which
// window.Rask.{TypeName} globals exist. Component lifecycle hooks (especially
// the first OnRenderedAsync) can race the bundle: they call into beginInvokeJS
// before the bundle has been injected, and identifier resolution for
// "Rask.X.method" would otherwise fault the awaiting Task with "Could not
// find ... on target". Defer Rask.*-prefixed calls until the gate opens, then
// drain. Non-Rask identifiers (sessionStorage.*, localStorage.*) are browser
// builtins and always present.
let scopedJsReady = false;
let pendingScopedInvokes = [];

// External Head-declared <script src> and <link rel=stylesheet> are tracked
// here so Rask.* JS invokes can wait until every declared dep has loaded
// before firing. Without this, a component invoking e.g. window.hljs in its
// OnRenderedAsync would have to hand-roll its own load-event workaround.
// The gate is global on purpose: components don't know about each other's
// deps, and per-invoke dependency declarations push API surface back onto
// users.
const pendingHeadAssets = new Set();
const trackedHeadAssets = new WeakSet();
const HEAD_ASSET_LOAD_TIMEOUT_MS = 5000;

function isAssetAlreadyLoaded(url) {
    if (!url || !window.performance || !performance.getEntriesByName) return false;
    const entries = performance.getEntriesByName(url);
    for (let i = 0; i < entries.length; i++) {
        if (entries[i].responseEnd > 0) return true;
    }
    return false;
}

function trackHeadAsset(el) {
    if (!el || el.nodeType !== 1 || trackedHeadAssets.has(el)) return;
    // The scoped tags have their own gate; don't double-track them. WASM marks
    // them data-rask-managed (the inline <style id="rask-scoped"> + inline
    // <script id="rask-scoped-js">); Server marks them data-rask-scoped.
    if (el.hasAttribute("data-rask-managed")
        || el.hasAttribute("data-rask-scoped")
        || el.hasAttribute("data-rask-scoped-js")) return;
    let url;
    if (el.tagName === "SCRIPT" && el.src) url = el.src;
    else if (el.tagName === "LINK" && el.rel === "stylesheet" && el.href) url = el.href;
    else return;
    trackedHeadAssets.add(el);
    if (isAssetAlreadyLoaded(url)) return;
    pendingHeadAssets.add(el);
    const done = () => {
        if (!pendingHeadAssets.delete(el)) return;
        maybeDrainPendingInvokes();
    };
    el.addEventListener("load", done, {once: true});
    el.addEventListener("error", done, {once: true});
    // Safety: the load event may have fired between insertion and our listener
    // attach (cache hit). The performance.getEntriesByName check covers most
    // cases; the timeout covers everything else so a missed load doesn't hold
    // Rask.* invokes forever.
    setTimeout(done, HEAD_ASSET_LOAD_TIMEOUT_MS);
}

function scanHeadAssets() {
    const els = document.head.querySelectorAll("script[src], link[rel=stylesheet]");
    for (let i = 0; i < els.length; i++) trackHeadAsset(els[i]);
}

function headAssetsReady() {
    return pendingHeadAssets.size === 0;
}

function maybeDrainPendingInvokes() {
    if (!scopedJsReady || !headAssetsReady()) return;
    if (pendingScopedInvokes.length === 0) return;
    const drained = pendingScopedInvokes;
    pendingScopedInvokes = [];
    for (let i = 0; i < drained.length; i++) {
        const c = drained[i];
        beginInvokeJS(c.taskId, c.identifier, c.argsJson, c.resultType, c.targetInstanceId);
    }
}

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
    // Initial sweep for Head-declared external assets emitted by the browser's
    // index.html (and any subsequent applyRender will re-sweep so morph-added
    // assets get picked up too — see applyDom in handle()).
    scanHeadAssets();
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
    // body runs the new `window.Rask[TypeName] = ...` assignments so user JS
    // modules pick up hot-reloaded code.
    const existing = document.getElementById("rask-scoped-js");
    if (existing && existing.parentNode) existing.parentNode.removeChild(existing);
    // Close the gate before injecting — even on hot reload the window.Rask
    // bindings briefly disappear while the new script body executes.
    scopedJsReady = false;
    const script = document.createElement("script");
    script.id = "rask-scoped-js";
    script.setAttribute("data-rask-managed", "");
    script.textContent = jsText;
    // Inline scripts execute synchronously on insertion, so by the time
    // appendChild returns the window.Rask.{TypeName} globals exist. Open the
    // gate and drain any calls queued during the closed window.
    document.head.appendChild(script);
    scopedJsReady = true;
    maybeDrainPendingInvokes();
}

// reviveScript() + morph() are concatenated in at build time by the
// _RaskSpliceClientJs target.
// @@RASK_MORPH@@

function applyHistory(history) {
    if (!history || typeof history.url !== "string") return;
    const target = prependBase(history.url);
    if (history.action === "replace")
        window.history.replaceState({rask: true}, "", target);
    else
        window.history.pushState({rask: true}, "", target);
}

// Comment nodes (nodeType 8) appear in document.childNodes for any HTML page
// that has a top-level <!-- ... --> (the WASM index.html shell has one). The
// server's frame walk only emits DOM-relevant kinds (Element=1, Text=3, Doctype=10,
// Raw which the browser materialises as either text or elements). Walking the
// raw childNodes would shift every path index by the comment count. Filter to
// match the server's view.
const _RELEVANT_NODE_TYPES = new Set([1 /*Element*/, 3 /*Text*/, 10 /*Doctype*/]);

function relevantChild(parent, index) {
    if (!parent || !parent.childNodes) return null;
    let seen = 0;
    for (let i = 0; i < parent.childNodes.length; i++) {
        const n = parent.childNodes[i];
        if (_RELEVANT_NODE_TYPES.has(n.nodeType)) {
            if (seen === index) return n;
            seen++;
        }
    }
    return null;
}

function resolvePath(path) {
    let node = document;
    for (let i = 0; i < path.length; i++) {
        node = relevantChild(node, path[i]);
        if (!node) return null;
    }
    return node;
}

// Mirror selected attribute writes onto the matching IDL property. After user
// interaction, an input's `value` attribute is the *default*, not the current
// state — setAttribute does not reach the live value. Same for `checked` on
// checkboxes/radios and `selected` on options. Skip the value-sync on the focused
// element so the diff doesn't clobber the user's in-flight typing during a server
// render that raced ahead of the latest keystroke.
function syncFormProperty(el, name, value, isPresent) {
    // `isPresent` separates set-vs-remove because `checked`/`selected` are
    // presence-based HTML attributes — `<input checked>`, `<input checked="">`,
    // `<input checked="checked">` all mean checked. RemoveAttribute → unchecked.
    if (!el) return;
    const tag = el.tagName;
    if (!tag) return;
    if (name === "value" && (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT")) {
        if (document.activeElement === el) return;
        el.value = value;
    } else if (name === "checked" && tag === "INPUT") {
        el.checked = !!isPresent;
    } else if (name === "selected" && tag === "OPTION") {
        el.selected = !!isPresent;
    }
}

// Diff codec interpreter — mirror of rask.js applyDiff. Applies ops produced by
// C#-side FrameDiffer.Diff to the live DOM. Each op is a positional JSON array;
// dispatch on op[0] (the kind) to know which trailing slots are present:
//   1 SetAttribute     [k, path, name|idx, value]
//   2 RemoveAttribute  [k, path, name|idx]
//   3 UpdateText       [k, path, value]
//   4 InsertSubtree    [k, path, html, domCount]
//   5 RemoveSubtree    [k, path, domCount]
//   6 MoveSubtree      [k, path, sourceSlot]
// Names for SetAttribute/RemoveAttribute may be a string (inline) or a number
// (index into the optional payload-level "names" array). The server interns
// names appearing 2+ times in the same payload to drop the duplicate string
// bytes.
function applyDiff(ops, names) {
    const resolveName = (raw) =>
        (typeof raw === "number" && names) ? names[raw] : raw;

    for (let i = 0; i < ops.length; i++) {
        const op = ops[i];
        const k = op[0];
        const path = op[1] || [];
        switch (k) {
            case 1: { // SetAttribute [k, path, name|idx, value]
                const el = resolvePath(path);
                if (el && el.setAttribute) {
                    const name1 = resolveName(op[2]);
                    const rawVal = op[3];
                    const newVal = rawVal == null ? "" : rawVal;
                    el.setAttribute(name1, newVal);
                    syncFormProperty(el, name1, newVal, true);
                }
                break;
            }
            case 2: { // RemoveAttribute [k, path, name|idx]
                const el2 = resolvePath(path);
                if (el2 && el2.removeAttribute) {
                    const name2 = resolveName(op[2]);
                    el2.removeAttribute(name2);
                    syncFormProperty(el2, name2, "", false);
                }
                break;
            }
            case 3: { // UpdateText [k, path, value]
                const tn = resolvePath(path);
                if (tn) {
                    const tv = op[2];
                    tn.textContent = tv == null ? "" : tv;
                }
                break;
            }
            case 4: { // InsertSubtree [k, path, html, domCount]
                const insertHtml = op[2];
                if (typeof insertHtml !== "string") {
                    console.warn("[Rask] InsertSubtree without payload — falling back to full reload");
                    location.reload();
                    return;
                }
                const parentPath = path.slice(0, path.length - 1);
                const slot = path[path.length - 1];
                const parent = resolvePath(parentPath);
                if (!parent) break;
                const tpl = document.createElement("template");
                tpl.innerHTML = insertHtml;
                const refNode = parent.childNodes[slot] || null;
                while (tpl.content.firstChild) parent.insertBefore(tpl.content.firstChild, refNode);
                break;
            }
            case 5: { // RemoveSubtree [k, path, domCount]
                const rmParentPath = path.slice(0, path.length - 1);
                const rmSlot = path[path.length - 1];
                const rmParent = resolvePath(rmParentPath);
                if (!rmParent) break;
                const n = op[2] || 1;
                for (let r = 0; r < n; r++) {
                    const v = rmParent.childNodes[rmSlot];
                    if (!v) break;
                    rmParent.removeChild(v);
                }
                break;
            }
            case 6: { // MoveSubtree [k, path, sourceSlot]
                // Path encodes parent + destination slot; op[2] is the source slot.
                // Detach the source FIRST, then resolve the destination refNode against
                // the post-detach sibling list — same coordinate model the server's
                // keyed differ uses when computing move targets.
                const mvParentPath = path.slice(0, path.length - 1);
                const mvDst = path[path.length - 1];
                const mvParent = resolvePath(mvParentPath);
                if (!mvParent) break;
                const mvSrcRaw = op[2];
                const mvSrc = mvSrcRaw == null ? 0 : mvSrcRaw;
                const mvNode = relevantChild(mvParent, mvSrc);
                if (!mvNode) break;
                mvParent.removeChild(mvNode);
                const mvRef = relevantChild(mvParent, mvDst);
                mvParent.insertBefore(mvNode, mvRef);
                break;
            }
            default:
                console.warn("[Rask] Unknown diff op kind: " + k);
                location.reload();
                return;
        }
    }
}

function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    // Diff-mode payload: apply ops directly against the live DOM. Still flow
    // history below so SPA navigation continues to work alongside the diff.
    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        applyDiff(reply.ops, Array.isArray(reply.names) ? reply.names : null);
        applyHistory(reply.history);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
        return;
    }
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
    // raskAfterMorph hook) runs inside the applyDom callback so dispatch reads the
    // freshly-morphed DOM rather than the pre-morph one.
    const applyDom = () => {
        if (freshHtml) {
            morph(document.documentElement, freshHtml);
            root = document.querySelector("[data-rask-root]") || document.body;
            // Pick up any newly-inserted Head-declared external assets so
            // their load events feed into the Rask.* invoke gate.
            scanHeadAssets();
        }
        applyHistory(reply.history);
        if (typeof reply.cssHash === "string" || reply.cssHash === null)
            applyScopedCss(reply.cssHash, reply.cssText);
        if (typeof reply.jsHash === "string" || reply.jsHash === null)
            applyScopedJs(reply.jsHash, reply.jsText);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    applyDom();
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

// ----- IJSRuntime bridge -----------------------------------------------------
// Called by Rask.Wasm.JSInterop.BeginInvokeJSImport (a [JSImport]). Walks the
// dotted identifier on `window`, invokes it with the JSON-decoded args, then
// ships the result back through the EndInvokeJSResult JSExport — same shape as
// the server-side dispatcher in rask.js.

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
    if (value && typeof value === "object" && typeof value.__jsObjectId === "number") {
        return jsObjectRefs.get(value.__jsObjectId);
    }
    return value;
}

function endInvokeJSResult(taskId, success, result, error) {
    if (!dotnetExports || !dotnetExports.Rask || !dotnetExports.Rask.Wasm
        || !dotnetExports.Rask.Wasm.JSInterop) return;
    const payload = success
        ? [Number(taskId), true, (result === undefined ? null : result)]
        : [Number(taskId), false, error || "JS invocation failed"];
    try {
        dotnetExports.Rask.Wasm.JSInterop.EndInvokeJSResult(JSON.stringify(payload));
    } catch (e) {
        console.error("[Rask] EndInvokeJSResult failed", e);
    }
}

export function beginInvokeJS(taskId, identifier, argsJson, resultType, targetInstanceId) {
    // Pre-bundle gate: a Rask.* identifier resolved against window before
    // applyScopedJs has executed the bundle returns null, which would surface
    // as a "Could not find ..." JSException on the awaiting Task. Park the
    // call until BOTH (a) the scoped-JS bundle has executed AND (b) every
    // Head-declared external <script>/<link> has loaded, then replay it.
    if (typeof identifier === "string"
        && identifier.indexOf("Rask.") === 0
        && (!scopedJsReady || !headAssetsReady())) {
        pendingScopedInvokes.push({taskId, identifier, argsJson, resultType, targetInstanceId});
        return;
    }
    Promise.resolve().then(() => {
        let args;
        try {
            args = JSON.parse(argsJson || "[]", jsReviver);
        } catch (e) {
            throw new Error("Failed to parse argsJson: " + e.message);
        }

        let target = window;
        const targetId = Number(targetInstanceId);
        if (targetId !== 0) {
            target = jsObjectRefs.get(targetId);
            if (!target) throw new Error("Unknown JS object reference: " + targetInstanceId);
        }

        const resolved = jsResolveIdentifier(target, identifier);
        if (!resolved) throw new Error("Could not find '" + identifier + "' on target");
        const parent = resolved[0];
        const key = resolved[1];
        const fn = parent[key];
        return (typeof fn === "function") ? fn.apply(parent, args) : fn;
    }).then((value) => {
        if (resultType === 3) {
            endInvokeJSResult(taskId, true, null);
            return;
        }
        if (resultType === 1) {
            const refId = nextJsObjectRefId++;
            jsObjectRefs.set(refId, value);
            endInvokeJSResult(taskId, true, {"__jsObjectId": refId});
            return;
        }
        endInvokeJSResult(taskId, true, value);
    }).catch((err) => {
        endInvokeJSResult(taskId, false, null, (err && err.message) || String(err));
    });
}

// ----- DotNet shim (mirror of Blazor's window.DotNet, for [JSInvokable]) -----
const dotNetPending = new Map();
let nextDotNetCallId = 1;

window.DotNet = window.DotNet || {
    invokeMethodAsync(assemblyName, methodIdentifier /*, ...args */) {
        const args = Array.prototype.slice.call(arguments, 2);
        const callId = String(nextDotNetCallId++);
        return new Promise((resolve, reject) => {
            dotNetPending.set(callId, {resolve, reject});
            if (!dotnetExports || !dotnetExports.Rask || !dotnetExports.Rask.Wasm
                || !dotnetExports.Rask.Wasm.JSInterop) {
                dotNetPending.delete(callId);
                reject(new Error("Rask.Wasm.JSInterop not ready"));
                return;
            }
            dotnetExports.Rask.Wasm.JSInterop.BeginDotNetInvoke(
                callId, assemblyName, methodIdentifier, 0, JSON.stringify(args));
        });
    }
};

export function endDotNetInvoke(resultJson) {
    let msg;
    try { msg = JSON.parse(resultJson); }
    catch (e) { console.error("[Rask] endDotNetInvoke: malformed JSON", e); return; }
    const pending = dotNetPending.get(msg.callId);
    if (!pending) return;
    dotNetPending.delete(msg.callId);
    if (msg.success) pending.resolve(msg.result);
    else pending.reject(new Error(msg.error || "DotNet invocation failed"));
}
