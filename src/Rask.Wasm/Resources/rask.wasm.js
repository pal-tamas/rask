// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

let dotnetExports = null;
let root = null;
let basePath = null;

// Built-in element-ref helpers, invoked from C# via ElementRef.FocusAsync/Blur/ScrollIntoView.
// The JSON reviver resolves an ElementRef arg to the live DOM element, so each receives it.
window.__raskEl = window.__raskEl || {
    focus: function (el) {
        if (el) el.focus();
    },
    blur: function (el) {
        if (el) el.blur();
    },
    scrollIntoView: function (el, opts) {
        if (el) el.scrollIntoView(opts || {behavior: "smooth", block: "nearest"});
    }
};

// Serializes render application across payloads. A navigation diff may defer its
// body swap until the new page's scoped CSS loads (applyHeadThenWaitForCss), which
// opens a microtask/timer gap during which .NET could deliver the next render. Both
// the diff and full-HTML paths chain through this tail promise so a deferred body
// always commits before the following payload's ops — paths in a later diff are
// computed against the render this one produces, so they must not be applied first.
let _renderQueue = Promise.resolve();

// The "#fragment" of an intercepted nav-link click. The fragment never leaves the
// browser (the navigate message carries only path+query, and the history url has no
// hash), so we stash it here on click and consume it when the matching push reply
// commits — scroll to that anchor, else to the top. Cleared on consume.
let _pendingScrollHash = "";

// scopedJsReady starts true: per-component scripts ship as
// <script src="/_rask/a/{hash}.js" defer> tags in the initial HTML's <head> (and
// are morphed in/out as components mount/unmount). The browser's defer semantics
// run them before DOMContentLoaded, which is well before any user click could
// trigger a Rask.* invoke. The legacy bundle-based gate (waiting for one big
// inline-injected script) is gone with the cssText/jsText payload fields. The
// pendingScopedInvokes queue is kept because the user-Head-declared CDN path
// (see pendingHeadAssets below) still needs to defer Rask.* calls until those
// external deps have loaded.
let scopedJsReady = true;
let pendingScopedInvokes = [];

// External Head-declared <script src> and <link rel=stylesheet> are tracked
// here so Rask.* JS invokes can wait until every declared dep has reached
// a terminal state — load, error, OR a 5-second safety timeout — before
// firing. Without this, a component invoking e.g. window.hljs in its
// OnRenderedAsync would have to hand-roll its own load-event workaround.
// The gate is global on purpose: components don't know about each other's
// deps, and per-invoke dependency declarations push API surface back onto
// users.
//
// CONTRACT: the gate guarantees the asset's terminal event has fired
// before draining queued Rask.* invokes — NOT that the asset loaded
// successfully. A failed asset (CDN flake, refresh cache miss, extension
// block, integrity mismatch, CSP) still terminates the gate via its
// 'error' event or the 5s timeout, and queued invokes run anyway. User
// JS that depends on a global the asset was meant to define MUST be
// defensive — e.g. `if (typeof window.hljs === "undefined") return;`.
// The framework logs a clear warning on the failure paths so the
// resulting TypeError isn't a mystery.
const pendingHeadAssets = new Set();
const trackedHeadAssets = new WeakSet();
const failedHeadAssets = new Set();
const HEAD_ASSET_LOAD_TIMEOUT_MS = 5000;
// Scoped /_rask/a/{hash}.js scripts are same-origin and effectively always fire a
// load/error event, so they only need a hang-backstop, not the short user-CDN
// contract. The window must comfortably exceed how long a cold scoped-JS load can lag
// behind the first-render Rask.* invoke on a constrained 2-core runner — a deep-link
// straight to a CodeSample page queues Rask.CodeSample.rendered before the per-component
// <script defer> has executed, and a short window force-faults the invoke into
// "Could not find ... on target" so highlighting never lands. A genuinely-missing asset
// (404) still surfaces fast: its <script> fires an 'error' event that drains the gate
// immediately, so the long window only ever applies to a slow-but-loading asset.
const SCOPED_ASSET_LOAD_TIMEOUT_MS = 30000;
// Hard cap on how long a navigation diff defers the body swap waiting for a newly
// mounted page's scoped stylesheet to load (see applyHeadThenWaitForCss). A warm,
// content-addressed /_rask/a/{hash}.css load resolves in a few ms; the cap only ever
// applies to a genuinely slow/failed sheet, where we'd rather show the (briefly
// unstyled) page than stall navigation.
const CSS_FOUC_GUARD_MS = 500;

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
    // Per-component scoped tags carry data-rask-key with the framework-reserved
    // "rsk-" prefix, served from /_rask/a/{hash}.{ext}. Scoped CSS (<link rsk-css->)
    // never defines a JS global, so it stays out of the invoke gate. Scoped JS
    // (<script rsk-js->) DOES define window.Rask.{Type}; it must be tracked so a
    // first-render Rask.* invoke waits for the script's actual load event rather
    // than racing a fixed poll timeout — on a constrained runner the cold scoped-JS
    // load can lag well past that window, which previously force-faulted the call.
    const key = el.getAttribute("data-rask-key");
    const isScoped = !!(key && key.indexOf("rsk-") === 0);
    let url;
    if (el.tagName === "SCRIPT" && el.src) url = el.src;
    else if (el.tagName === "LINK" && el.rel === "stylesheet" && el.href) url = el.href;
    else return;
    if (isScoped && el.tagName !== "SCRIPT") return;
    // A same-origin asset (scoped /_rask/a/* OR a vendored user-Head script like a
    // self-hosted highlight.min.js) is reliable but can load slowly on a constrained
    // cold boot; it gets the generous hang-backstop. Only a true cross-origin CDN keeps
    // the short 5s contract (a dead CDN must not hold Rask.* invokes for 30s). A failed
    // same-origin asset still fires 'error' quickly, so the longer window only ever
    // applies to a genuinely slow-but-loading asset.
    const sameOrigin = typeof url === "string" && url.indexOf(location.origin) === 0;
    const useLongBackstop = isScoped || sameOrigin;
    trackedHeadAssets.add(el);
    if (isAssetAlreadyLoaded(url)) return;
    pendingHeadAssets.add(el);
    const finish = (outcome) => {
        if (!pendingHeadAssets.delete(el)) return;
        if (outcome === "error" || outcome === "timeout") {
            failedHeadAssets.add(url);
            const reason = outcome === "error"
                ? "fired 'error' event (network failure / blocked / integrity mismatch / CSP)"
                : `did not fire load/error within ${HEAD_ASSET_LOAD_TIMEOUT_MS}ms — proceeding anyway`;
            // console.warn rather than .error: the page CAN still render
            // (the user's defensive code is the contract). Surface enough
            // context that the consequent TypeError in user JS is traceable
            // back to the asset that failed.
            console.warn(`[Rask] Head asset (${el.tagName.toLowerCase()}) ${url} ${reason}. ` +
                "Queued Rask.* invokes will run; user JS depending on this asset's global must be defensive.");
        }
        maybeDrainPendingInvokes();
    };
    el.addEventListener("load", () => finish("load"), {once: true});
    el.addEventListener("error", () => finish("error"), {once: true});
    // Safety: the load/error event may have fired between insertion and our
    // listener attach (cache hit). The performance.getEntriesByName check
    // covers most cases; the timeout covers everything else so a missed
    // event doesn't hold Rask.* invokes forever. Same-origin assets get a generous
    // hang-backstop (a slow same-origin load is legitimate); cross-origin CDNs keep
    // the shorter contract.
    setTimeout(() => finish("timeout"), useLongBackstop ? SCOPED_ASSET_LOAD_TIMEOUT_MS : HEAD_ASSET_LOAD_TIMEOUT_MS);
}

function scanHeadAssets() {
    const els = document.head.querySelectorAll("script[src], link[rel=stylesheet]");
    for (let i = 0; i < els.length; i++) trackHeadAsset(els[i]);
}

function headAssetsReady() {
    return pendingHeadAssets.size === 0;
}

// Morph the incoming <head> into the live one, then return a Promise that resolves
// once every *newly added* stylesheet has reached a terminal state (load / error /
// CSS_FOUC_GUARD_MS timeout). On a navigation diff the body ops must not be applied
// until the new page's scoped CSS (<link href="/_rask/a/{hash}.css">) is in place,
// or the freshly-swapped body paints unstyled for a beat (FOUC). Stylesheets already
// present + loaded before the morph — the warm-cache case — are skipped via
// isAssetAlreadyLoaded, so a return of null means "nothing to wait for, apply the
// body synchronously" and warm navigations keep today's instant timing.
function applyHeadThenWaitForCss(freshHead) {
    const before = new Set();
    document.head.querySelectorAll('link[rel="stylesheet"]').forEach((l) => {
        if (l.href && isAssetAlreadyLoaded(l.href)) before.add(l.href);
    });
    morph(document.head, freshHead);
    const pending = [];
    document.head.querySelectorAll('link[rel="stylesheet"]').forEach((l) => {
        if (!l.href || before.has(l.href) || isAssetAlreadyLoaded(l.href)) return;
        pending.push(new Promise((resolve) => {
            const done = () => resolve();
            l.addEventListener("load", done, {once: true});
            l.addEventListener("error", done, {once: true});
            setTimeout(done, CSS_FOUC_GUARD_MS);
        }));
    });
    return pending.length ? Promise.all(pending) : null;
}

function maybeDrainPendingInvokes() {
    if (!scopedJsReady || !headAssetsReady()) return;
    if (pendingScopedInvokes.length === 0) return;
    // Re-queue any whose Rask.{Name} namespace still hasn't appeared — they'll be drained
    // by the polling loop below when (if) the per-component script eventually loads.
    const stillWaiting = [];
    const ready = [];
    for (let i = 0; i < pendingScopedInvokes.length; i++) {
        const c = pendingScopedInvokes[i];
        if (raskNamespaceReady(c.identifier)) ready.push(c);
        else stillWaiting.push(c);
    }
    pendingScopedInvokes = stillWaiting;
    for (let i = 0; i < ready.length; i++) {
        const c = ready[i];
        beginInvokeJS(c.taskId, c.identifier, c.argsJson, c.resultType, c.targetInstanceId);
    }
}

// Returns true when `Rask.{Name}` is populated on window (for "Rask.{Name}.{method}"
// identifiers), or true when the identifier doesn't follow the Rask.* pattern. Lets
// beginInvokeJS distinguish "the per-component script hasn't loaded yet — park me"
// from "ready to dispatch".
function raskNamespaceReady(identifier) {
    if (typeof identifier !== "string") return true;
    if (identifier.indexOf("Rask.") !== 0) return true;
    const rest = identifier.substring(5);
    const dot = rest.indexOf(".");
    const name = dot < 0 ? rest : rest.substring(0, dot);
    return !!(window.Rask && window.Rask[name]);
}

// Per-component scripts load asynchronously over HTTP from /_rask/a/{hash}.js. A first-
// render OnRenderedAsync calling Rask.X.method races the script's load event; the parked
// invoke needs a way to wake up when window.Rask.X appears. A 100ms poll catches the
// common cache-warm-load path and times out on genuinely-missing namespaces (those calls
// then surface "Could not find" as documented, rather than hanging forever).
//
// The timeout matches the scoped-asset load backstop (SCOPED_ASSET_LOAD_TIMEOUT_MS): on a
// constrained cold boot (e.g. the 2-core CI runner) the per-component bundle can execute
// several seconds after the first-render invoke is queued, and when its <script> isn't yet
// tracked as a pending head asset, headAssetsReady() is true — so a short 5s window would
// force-fault "Could not find 'Rask.X.method' on target" and trip RootErrorBoundary while
// the bundle was merely still loading. The longer window lets the namespace appear first.
const RASK_NAMESPACE_POLL_INTERVAL_MS = 100;
const RASK_NAMESPACE_POLL_TIMEOUT_MS = SCOPED_ASSET_LOAD_TIMEOUT_MS;
let raskNamespacePollHandle = 0;
let raskNamespacePollStarted = 0;

function ensureRaskNamespacePoll() {
    if (raskNamespacePollHandle !== 0) return;
    raskNamespacePollStarted = Date.now();
    raskNamespacePollHandle = setInterval(() => {
        const timedOut = Date.now() - raskNamespacePollStarted > RASK_NAMESPACE_POLL_TIMEOUT_MS;
        // Force-dispatch only once there's nothing left to wait for: the queue drained,
        // OR the poll timed out AND every tracked head/scoped asset has reached a
        // terminal state. The headAssetsReady() guard is what keeps a still-loading
        // scoped /_rask/a/{hash}.js from being faulted prematurely on a slow runner —
        // its load event drains the queue normally; a genuinely missing/errored
        // namespace still surfaces "Could not find" once its script terminates.
        if (pendingScopedInvokes.length === 0 || (timedOut && headAssetsReady())) {
            // Time's up: drain whatever's left through beginInvokeJS — the missing-namespace
            // calls will surface their original "Could not find" JSException, which the
            // component's ErrorBoundary catches. Better than hanging forever.
            clearInterval(raskNamespacePollHandle);
            raskNamespacePollHandle = 0;
            const drained = pendingScopedInvokes;
            pendingScopedInvokes = [];
            for (let i = 0; i < drained.length; i++) {
                const c = drained[i];
                dispatchUnparked(c.taskId, c.identifier, c.argsJson, c.resultType, c.targetInstanceId);
            }
            return;
        }
        maybeDrainPendingInvokes();
    }, RASK_NAMESPACE_POLL_INTERVAL_MS);
}

// Read once from <base href> (or the page URL if no <base> is set) so the
// runtime can host under a sub-path like /Rask/ on GitHub Pages without the
// .NET side ever seeing the prefix. Resolves to the directory portion so a
// page URL like /index.html yields "/" (not "/index.html/").
export function getBasePath() {
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

// reviveScript() + morph() are concatenated in at build time by the
// _RaskSpliceClientJs target.
// @@RASK_MORPH@@

function applyHistory(history) {
    if (!history || typeof history.url !== "string") return;
    let target = prependBase(history.url);
    if (history.action === "replace") {
        window.history.replaceState({rask: true}, "", target);
    } else {
        if (_pendingScrollHash) target += _pendingScrollHash;
        window.history.pushState({rask: true}, "", target);
    }
}

// Reset scroll on forward navigation only (history.action "push" — a nav-link click
// or Navigator.Navigate). "replace" (Back/Forward popstate, SetQuery, auth redirect)
// is left to the browser's native scroll restoration. When the intercepted link
// carried a "#fragment" matching an element, scroll there instead of the top.
// Call this only after the new body has committed so the anchor target exists.
function applyNavScroll(history) {
    if (!history || history.action === "replace") {
        _pendingScrollHash = "";
        return;
    }
    const hash = _pendingScrollHash;
    _pendingScrollHash = "";
    if (hash && hash.length > 1) {
        let el = null;
        try {
            el = document.querySelector(hash) ||
                document.getElementById(decodeURIComponent(hash.slice(1)));
        } catch (e) {
            el = null;
        }
        if (el) {
            el.scrollIntoView();
            return;
        }
    }
    window.scrollTo(0, 0);
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

// Like relevantChild but counts as if `skip` were already gone — the post-detach
// coordinate the keyed differ uses for move targets. Lets us resolve the anchor
// WITHOUT detaching the moving node, so the move can run as a single relocation.
function relevantChildSkipping(parent, index, skip) {
    if (!parent || !parent.childNodes) return null;
    let seen = 0;
    for (let i = 0; i < parent.childNodes.length; i++) {
        const n = parent.childNodes[i];
        if (n === skip) continue;
        if (_RELEVANT_NODE_TYPES.has(n.nodeType)) {
            if (seen === index) return n;
            seen++;
        }
    }
    return null;
}

// Relocate `node` before `ref` under `parent`. Prefer the Atomic Move API
// (moveBefore, Chromium 133+): it moves the node WITHOUT disconnecting it, so a
// focused descendant keeps its focus, selection, and caret across a keyed reorder.
// removeChild+insertBefore — and even a bare insertBefore — disconnect the node
// and blur it, which silently broke the "survivors keep their DOM state" contract.
// Fall back to insertBefore where moveBefore is unavailable or rejects the move.
function moveChildBefore(parent, node, ref) {
    if (parent.moveBefore) {
        try {
            parent.moveBefore(node, ref);
            return;
        } catch {
            // Not connected / cross-document — fall through to insertBefore.
        }
    }
    parent.insertBefore(node, ref);
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
        if (raskShouldSuppressValue(el, value)) return;
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
                // Scripts parsed via innerHTML carry the "already started" flag and will
                // NOT execute when inserted into the live document. Rebuild them via
                // reviveScript so a scoped <script src="/_rask/a/{hash}.js"> (or a user
                // Head <script>) delivered through a keyed InsertSubtree diff actually
                // runs — otherwise its window.Rask.{Type}/global never appears. Mirrors
                // the full-HTML morph path, which already revives inserted scripts.
                const insertScripts = tpl.content.querySelectorAll("script");
                for (let si = 0; si < insertScripts.length; si++) {
                    const oldScript = insertScripts[si];
                    oldScript.parentNode.replaceChild(reviveScript(oldScript), oldScript);
                }
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
                // The destination slot is in the server's post-detach coordinate (the live
                // DOM with the moved node removed), so resolve the anchor by SKIPPING the
                // moving node rather than detaching it — then relocate with moveChildBefore
                // so a focused descendant keeps focus/selection across the reorder.
                const mvParentPath = path.slice(0, path.length - 1);
                const mvDst = path[path.length - 1];
                const mvParent = resolvePath(mvParentPath);
                if (!mvParent) break;
                const mvSrcRaw = op[2];
                const mvSrc = mvSrcRaw == null ? 0 : mvSrcRaw;
                const mvNode = relevantChild(mvParent, mvSrc);
                if (!mvNode) break;
                const mvRef = relevantChildSkipping(mvParent, mvDst, mvNode);
                moveChildBefore(mvParent, mvNode, mvRef);
                break;
            }
            case 7: { // PermutationBatch [k, parentPath, moves] — moves = [dst0,src0,dst1,src1,…]
                // path IS the parent (no trailing slot to split off). Replay each (dst,src)
                // pair in array order: the server computed every pair against the live DOM
                // as mutated by the preceding pairs, so order is load-bearing — never reorder.
                // Each dst is a post-detach slot, so resolve the anchor by skipping the moving
                // node and relocate with moveChildBefore (preserves focus across the reorder).
                const pbParent = resolvePath(path);
                if (!pbParent) break;
                const pbMoves = op[2] || [];
                for (let m = 0; m + 1 < pbMoves.length; m += 2) {
                    const pbDst = pbMoves[m];
                    const pbSrc = pbMoves[m + 1];
                    const pbNode = relevantChild(pbParent, pbSrc);
                    if (!pbNode) continue;
                    const pbRef = relevantChildSkipping(pbParent, pbDst, pbNode);
                    moveChildBefore(pbParent, pbNode, pbRef);
                }
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
    // Diff-mode payload: apply ops directly against the live DOM. Both paths chain
    // through _renderQueue so a diff that defers its body for a CSS load can't be
    // overtaken by the next payload (see _renderQueue).
    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        _renderQueue = _renderQueue.then(() => applyDiffReply(reply), () => applyDiffReply(reply));
        return;
    }
    _renderQueue = _renderQueue.then(() => applyFullReply(reply), () => applyFullReply(reply));
}

function applyDiffReply(reply) {
    // The head isn't in the diff frame stream (user Head contributions are collected +
    // spliced render-side), so a head change rides the payload as a <head> fragment.
    // Morph it into document.head FIRST — keyed reconciliation (data-rask-key) keeps
    // unchanged scoped-CSS links, and morph skips data-rask-managed boot bundles so they
    // survive. When the new page adds a not-yet-cached scoped stylesheet, defer the body
    // ops until it loads so the swapped body never paints unstyled (FOUC).
    const applyBody = () => {
        applyDiff(reply.ops, Array.isArray(reply.names) ? reply.names : null);
        applyHistory(reply.history);
        applyNavScroll(reply.history);
        // A diff can insert Head-declared external <script>/<link> and scoped-JS tags
        // (keyed InsertSubtree). Track them so their load events feed the Rask.* invoke
        // gate, then drain anything now unblocked — the full-HTML morph path does the same.
        scanHeadAssets();
        maybeDrainPendingInvokes();
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    if (typeof reply.head === "string") {
        const freshHead = new DOMParser().parseFromString(reply.head, "text/html").head;
        const wait = freshHead ? applyHeadThenWaitForCss(freshHead) : null;
        if (wait) return wait.then(applyBody);
    }
    applyBody();
}

function applyFullReply(reply) {
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
        // Scoped CSS/JS arrives in the morphed HTML as
        // <link href="/_rask/a/{hash}.css"> / <script src="/_rask/a/{hash}.js" defer>
        // tags — no payload-side cssText/jsText injection. Browser handles load
        // semantics via standard <link>/<script> lifecycle.
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
        // For a checkbox the meaningful state is el.checked, not el.value (the static "on"
        // default). Report it as "true"/"false" so bound checkboxes set the model to the
        // actual state (self-correcting). Radios/text keep sending el.value.
        const changeVal = (t.tagName === "INPUT" && t.type === "checkbox")
            ? (t.checked ? "true" : "false")
            : t.value;
        // Record the PRE-EDIT value (the last server-rendered `value` attribute) so a
        // lagging re-render carrying that stale value can't clobber the user's fresh
        // edit before the server's authoritative response lands — see
        // raskShouldSuppressValue. Checkboxes self-correct via the checked path, so
        // they stay out of the value guard.
        if (!(t.tagName === "INPUT" && t.type === "checkbox")) {
            const sv = t.getAttribute("value");
            raskNotePendingValue(t, sv === null ? "" : sv);
        }
        send({id: t.getAttribute("data-rask-on-change"), type: "change", value: changeVal});
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

// ----- Drag & drop -----------------------------------------------------------
// HTML5 native DnD bound to parameterless C# handlers (same dispatch path as click). The dragged
// item's identity rides the handler's closure, not the payload, so messages carry only {id,type}.
// dragstart seeds dataTransfer so the drag is valid in Firefox; dragover must preventDefault on a
// drop target or the browser rejects the drop. The optional data-rask-on-dragover round-trip
// drives a server-rendered drop-target highlight — deduped to one message per hovered element.
let lastDragOverEl = null;

document.addEventListener("dragstart", (e) => {
    const t = e.target.closest("[data-rask-on-dragstart]");
    if (!t || !inRoot(t)) return;
    if (e.dataTransfer) {
        try {
            e.dataTransfer.setData("text/plain", "");
        } catch (err) {
        }
        e.dataTransfer.effectAllowed = "move";
    }
    lastDragOverEl = null;
    send({id: t.getAttribute("data-rask-on-dragstart"), type: "dragstart"});
});

document.addEventListener("dragover", (e) => {
    const t = e.target.closest("[data-rask-on-drop], [data-rask-on-dragover]");
    if (!t || !inRoot(t)) return;
    // preventDefault is what marks this element as a valid drop target.
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = "move";
    if (!t.hasAttribute("data-rask-on-dragover")) return;
    if (t === lastDragOverEl) return; // dedupe: only notify when the hovered target changes
    lastDragOverEl = t;
    send({id: t.getAttribute("data-rask-on-dragover"), type: "dragover"});
});

document.addEventListener("drop", (e) => {
    const t = e.target.closest("[data-rask-on-drop]");
    if (!t || !inRoot(t)) return;
    e.preventDefault();
    lastDragOverEl = null;
    send({id: t.getAttribute("data-rask-on-drop"), type: "drop"});
});

document.addEventListener("dragend", (e) => {
    lastDragOverEl = null;
    const t = e.target.closest("[data-rask-on-dragend]");
    if (!t || !inRoot(t)) return;
    send({id: t.getAttribute("data-rask-on-dragend"), type: "dragend"});
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
    if (value && typeof value === "object") {
        if (typeof value.__jsObjectId === "number") {
            return jsObjectRefs.get(value.__jsObjectId);
        }
        // ElementRef: {"__raskRef__":"id"} -> the live DOM element (or null if not in the DOM).
        if (typeof value.__raskRef__ === "string") {
            return document.querySelector('[data-rask-ref="' + value.__raskRef__ + '"]');
        }
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
    // Two gates for Rask.* identifiers:
    //  1. headAssetsReady() — user-Head-declared CDN <script>/<link> deps still loading.
    //  2. raskNamespaceReady() — the component's per-component script
    //     (/_rask/a/{hash}.js, served by the host endpoint) hasn't executed yet, so
    //     window.Rask.{TypeName} doesn't exist. First-render OnRenderedAsync races this
    //     load; the parked invoke wakes up via the polling tick when the script's IIFE
    //     populates window.Rask.{TypeName}.
    if (typeof identifier === "string"
        && identifier.indexOf("Rask.") === 0
        && (!scopedJsReady || !headAssetsReady() || !raskNamespaceReady(identifier))) {
        pendingScopedInvokes.push({taskId, identifier, argsJson, resultType, targetInstanceId});
        ensureRaskNamespacePoll();
        return;
    }
    dispatchUnparked(taskId, identifier, argsJson, resultType, targetInstanceId);
}

function dispatchUnparked(taskId, identifier, argsJson, resultType, targetInstanceId) {
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
    try {
        msg = JSON.parse(resultJson);
    } catch (e) {
        console.error("[Rask] endDotNetInvoke: malformed JSON", e);
        return;
    }
    const pending = dotNetPending.get(msg.callId);
    if (!pending) return;
    dotNetPending.delete(msg.callId);
    if (msg.success) pending.resolve(msg.result);
    else pending.reject(new Error(msg.error || "DotNet invocation failed"));
}
