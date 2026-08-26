// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

let dotnetExports = null;
let root = null;
let basePath = null;

// Shared framework interop helpers (__raskEl, __raskApi) spliced from
// Rask.Core/Resources/rask-api.js at build time — single source across both transports.
// @@RASK_API@@

// Transport-agnostic PWA helpers (__raskPush/__raskNotify/__raskBadge/__raskWakeLock) spliced from
// Rask.Core/Resources/rask-pwa.js — the same source the Server client uses.
// @@RASK_PWA@@

// WASM-only helpers (__raskPwa.applyManifest, __raskInstall, device APIs) spliced from
// Rask.Wasm/Resources/rask-wasm-api.js — never ship in the Server client, since these back APIs that
// can't work over the WebSocket round-trip (or need WASM-only boot behaviour).
// @@RASK_WASM_API@@

// Dev-only "hot reload applied" indicator, spliced from Rask.Core/Resources/rask-hotreload.js — the
// same source the Server client uses. It exposes window.__raskHotReloadPill; here the
// notification arrives through the hotReloadApplied() export below rather than over a socket, because
// a WASM app has no Rask server to push it a frame.
// @@RASK_HOTRELOAD@@

// The development error panel (showDevError / hideDevError), shared with the Server runtime.
// @@RASK_DEVERROR@@

// Serializes render application across payloads. A navigation diff/full reply may defer
// its body swap until the new page's scoped CSS applies (waitForUnappliedHeadCss /
// preloadNewHeadStylesheets), opening a microtask/timer gap during which .NET could
// deliver the next render. Both
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
// CSS_FOUC_GUARD_MS + the scoped-CSS FOUC gating functions (waitForUnappliedHeadCss /
// preloadNewHeadStylesheets) are spliced in below from rask-scoped.js (@@RASK_SCOPED@@).

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
    // A scoped (rsk-) script must wait for its real load event before draining Rask.*
    // invokes: the eager <link rel="prefetch" as="script"> warms the HTTP cache and creates
    // a Resource Timing entry, but downloaded != executed — window.Rask.{Type} is only
    // defined once the script actually runs. Trusting timing here would let a first-render
    // invoke dispatch before execution and fault with "Could not find Rask.{Type}". For a
    // genuine warm non-scoped user-Head asset, "downloaded" stays an acceptable proxy (the
    // user's defensive code is the contract), so it keeps the fast path.
    if (!isScoped && isAssetAlreadyLoaded(url)) return;
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

// Scoped-CSS FOUC gating: CSS_FOUC_GUARD_MS + waitForUnappliedHeadCss (diff path) +
// preloadNewHeadStylesheets (full-HTML path) — spliced from Rask.Core/Resources/rask-scoped.js,
// shared with rask.js.
// @@RASK_SCOPED@@

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
    // Report the boot only when it went wrong. A success line here is one per session and told nobody
    // anything; an unreachable Dispatch is a dead app, and without this the first symptom is a click
    // that silently does nothing.
    if (!ok) {
        console.error("[Rask] setExports: the .NET Dispatch export is unreachable — no event will "
            + "reach the app. Exports:", exports);
        // And say so on the page. An app whose Dispatch is unreachable boots, paints, and then
        // ignores every click, which reads as a UI bug rather than as the build problem it is.
        bootFailed("The app's .NET event dispatcher is unreachable, so nothing on the page will "
            + "respond. This usually means Rask.Wasm.dll was trimmed away or failed to load.");
    }
    // Initial sweep for Head-declared external assets emitted by the browser's
    // index.html (and any subsequent applyRender will re-sweep so morph-added
    // assets get picked up too — see applyDom in handle()).
    scanHeadAssets();

    // Let registered IHostedServices drain when the page really goes away — the browser's nearest
    // thing to SIGTERM. `pagehide` rather than `beforeunload` because it also fires on mobile, where
    // a tab is far likelier to be discarded than closed.
    //
    // `event.persisted` is the whole reason this isn't a one-liner: it means the page is going into
    // the back/forward cache and can be restored, still running, with its services still needed.
    // Stopping them there would leave a restored page with dead background work and no way to notice.
    // Not registered with `once`, so a bfcache round-trip still gets drained on the eventual real
    // teardown; StopAsync is idempotent, so a double fire is harmless.
    window.addEventListener("pagehide", (event) => {
        if (event.persisted) return;
        try {
            exports?.Rask?.Wasm?.JSInterop?.StopHostedServices?.();
        } catch (e) {
            // Nothing useful can be done while the page is unloading, and throwing here would take
            // the rest of the browser's teardown with it.
            console.warn("[Rask] pagehide: stopping hosted services failed", e);
        }
    });
}

// Called by .NET (via [JSImport]) for both the initial paint and subsequent
// background re-renders. `payload` is a MemoryView — a zero-copy view over the
// UTF-8 JSON frame in the C# write buffer (built via LivePayload.BuildPayloadUtf8WithRoot,
// same shape as the WS frame the server emits). `.slice()` materialises a Uint8Array
// copy on the JS side (the one unavoidable copy, replacing the prior per-frame byte[]
// the C# side used to allocate); TextDecoder + JSON.parse then run on that. `.slice()`
// is also valid on a Uint8Array, so this stays correct if ever called with one directly.
const _payloadDecoder = new TextDecoder("utf-8");

export function applyRender(payload) {
    if (!payload || payload.length === 0) return;
    let reply;
    try {
        reply = JSON.parse(_payloadDecoder.decode(payload.slice()));
    } catch (e) {
        console.error("[Rask] applyRender: malformed payload", e);
        // Dropping a frame mid-session loses one update; dropping the FIRST one means the document
        // is never morphed and the boot screen stays up for ever. bootFailed decides which of the
        // two this is — it does nothing once the app has painted.
        bootFailed("The first frame from the app could not be read.", String(e));
        return;
    }
    handle(reply);
}

/**
 * Report a failure that leaves the app unusable, to whatever surface the shell's bootstrap
 * installed. main.js owns the rendering (it is loaded by every shell, including ones written
 * before this existed, and it is reachable even when this module is not); this is the seam the
 * rest of the framework — and .NET, via the `bootFailed` JSImport — reaches it through.
 *
 * A no-op when the page has already painted, and when a shell has no bootstrap of ours at all.
 */
export function bootFailed(message, detail) {
    const report = globalThis.__raskBootFailed;
    if (typeof report === "function") report(message, detail);
    else console.error(`[Rask] boot failed: ${message}`, detail ?? "");
}

// Dev-only. Called from .NET (WasmHotReloadBridge) once the hot-reload coordinator has finished
// applying an update and every open session has repainted — the WASM analogue of the Server's
// {"type":"hotReload","status":"applied"} frame. Purely an indicator: the DOM was already updated by
// the repaint that preceded this call, so it must not touch the tree.
//
// Guarded because the pill is spliced in from a shared module: if a future build ever drops the
// splice, a missing indicator should not throw across the interop boundary into .NET.
export function hotReloadApplied() {
    if (window.__raskHotReloadPill) window.__raskHotReloadPill();
}

// The input[type=file] ref registry (rask-files.js), shared with the Server client.
// @@RASK_FILES@@

// The [JSImport] on the .NET side binds to this module export, so the export has to be declared here (an ES
// module cannot re-export a spliced-in function declaration by name from an inner scope). The registry and
// the read itself are shared.
export function readFileChunk(ref, offset, length) {
    return raskReadFileChunk(ref, offset, length);
}

function registerFiles(inputEl, files) {
    return raskRegisterFiles(inputEl, files);
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

// The visitor's language signals, read in one synchronous call so the culture is settled before the
// first render rather than corrected in a second frame.
//
// No regex literals in here on purpose: this file is spliced and minified by RaskMinifyJs, whose
// tokenizer does not reliably distinguish a regex literal from division. Plain string work is not
// worth a mis-parse that would only show up in a published bundle.
export function getCultureSignals() {
    let cookie = null;
    try {
        const parts = document.cookie ? document.cookie.split("; ") : [];
        const prefix = ".AspNetCore.Culture=";
        for (let i = 0; i < parts.length; i++) {
            if (parts[i].indexOf(prefix) === 0) {
                cookie = decodeURIComponent(parts[i].slice(prefix.length));
                break;
            }
        }
    } catch (e) {
        // A document with cookies disabled throws on access rather than answering empty.
    }
    let query = null;
    try {
        query = new URLSearchParams(location.search).get("culture");
    } catch (e) {
    }
    return JSON.stringify({
        query: query,
        cookie: cookie,
        languages: (navigator.languages && navigator.languages.length)
            ? Array.prototype.slice.call(navigator.languages)
            : (navigator.language ? [navigator.language] : []),
    });
}

export function getBaseAddress() {
    // The app root (origin + base path), NOT document.baseURI. document.baseURI reflects the
    // *current* SPA route once the app has navigated (the <base> element is not in the live DOM
    // after boot), so reading it here would bake whatever route happened to be active when the
    // singleton HttpClient was first resolved into its BaseAddress — e.g. a fetch of
    // "data/posts-1.json" from a two-segment route like /guides/elements would resolve against
    // /guides/ and 404. getBasePath() is cached from the boot-time <base href> (carrying any
    // sub-path) and is route-independent, so the base stays the app root for the app's lifetime.
    return new URL(getBasePath(), location.origin).href;
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

// @@RASK_DOM@@

function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    // A development fault the app survived, riding the render payload (see the Server runtime for why
    // it is a field rather than a frame). Applied before either render path: the panel is a sibling of
    // the app, so it must not wait on the render queue.
    if (reply.devError) showDevError(reply.devError);
    // Diff-mode payload: apply ops directly against the live DOM. Both paths chain
    // through _renderQueue so a diff that defers its body for a CSS load can't be
    // overtaken by the next payload (see _renderQueue).
    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        _renderQueue = _renderQueue.then(() => applyDiffReply(reply), () => applyDiffReply(reply));
        return;
    }
    _renderQueue = _renderQueue.then(() => applyFullReply(reply), () => applyFullReply(reply));
}

// Per-invoke executor for the shared applyFrameInvokes loop (rask-dom.js). A frame's jsInvokes run
// AFTER applyDiff/morph patched the DOM — so a queued OnRenderedAsync focus acts on the committed
// DOM (e.g. a <dialog> that just gained its `open` attribute), the same post-commit ordering the
// Server has. beginInvokeJS runs the call and returns its result via the endInvokeJSResult JSExport.
function dispatchWasmInvoke(inv) {
    beginInvokeJS(
        String(inv.id),
        inv.identifier,
        typeof inv.argsJson === "string" ? inv.argsJson : null,
        typeof inv.resultType === "number" ? inv.resultType : 0,
        typeof inv.targetInstanceId === "number" ? String(inv.targetInstanceId) : "0");
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
        applyFrameInvokes(reply, dispatchWasmInvoke);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    if (typeof reply.head === "string") {
        const freshHead = new DOMParser().parseFromString(reply.head, "text/html").head;
        if (freshHead) {
            morph(document.head, freshHead);
            const wait = waitForUnappliedHeadCss();
            if (wait) return wait.then(() => window.__raskVt.run(applyBody));
        }
    }
    return window.__raskVt.run(applyBody);
}

function applyFullReply(reply) {
    let freshHtml = null;
    if (typeof reply.html === "string" && reply.html.length > 0) {
        const doc = new DOMParser().parseFromString(reply.html, "text/html");
        // Morph the whole <html> element so head changes (title, stylesheet links,
        // scoped-css link) propagate too — a full frame carries the whole document,
        // not just <body>, and <head> is where those changes are. Note the cost: the
        // morph removes any attribute the rendered <html> does not carry, so anything
        // a pre-boot script stamps on document.documentElement (a theme attribute, say)
        // must be re-applied from window.raskAfterMorph below. The bootstrap
        // <script src="main.js"> in the original
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
        // Cross-route navigation in WASM commits via this full-HTML morph (not the
        // diff path), so the scroll reset / fragment scroll must run here too — the
        // new body has just committed, so the anchor target exists.
        applyNavScroll(reply.history);
        // Scoped CSS/JS arrives in the morphed HTML as
        // <link href="/_rask/a/{hash}.css"> / <script src="/_rask/a/{hash}.js" defer>
        // tags — no payload-side cssText/jsText injection. Browser handles load
        // semantics via standard <link>/<script> lifecycle.
        applyFrameInvokes(reply, dispatchWasmInvoke);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
        if (reply.download) triggerDownload(reply.download);
    };
    // FOUC guard: preload any new scoped stylesheet the incoming document adds so the morph
    // paints the styled body only once its sheet has applied (see preloadNewHeadStylesheets).
    // Returns null — and we commit synchronously, at today's timing — when the render mounts
    // no new scoped CSS.
    if (freshHtml) {
        const wait = preloadNewHeadStylesheets(freshHtml);
        if (wait) return wait.then(() => window.__raskVt.run(applyDom));
    }
    return window.__raskVt.run(applyDom);
}

// Cached at module scope: TextEncoder construction is cheap but not free, and a
// steady-typing user fires `send` ~60×/sec via the rAF input-coalescing path.
const _sendEncoder = new TextEncoder();

async function send(payload) {
    // Deliberately not traced. `payload` carries the event's value — everything the user types — and
    // this runs ~60×/sec via the rAF coalescing path, so a log here writes form input to the console
    // of every production build. Debug a dispatch with a breakpoint, not by shipping one.
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
    // Stash the link's "#fragment" so applyNavScroll can scroll to the anchor once
    // the new page commits (the fragment is not sent to the server).
    _pendingScrollHash = url.hash || "";
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
    // A submit/reset button is driven by native form submission (handled by the dedicated submit
    // listener). Don't let an ANCESTOR click handler (e.g. a modal's .modal-dialog shield) hijack it
    // and cancel the default — that would break the form submit. A handler on the button itself still
    // runs: note `button.type` defaults to "submit" for a bare <button>, so gating on the ancestor
    // (t !== btn) is what keeps a plain Button(OnClick:) working here. Mirrors rask.js.
    const btn = e.target.closest("button, input");
    if (btn && btn !== t && (btn.type === "submit" || btn.type === "reset")) return;
    e.preventDefault();
    flushInputsNow();
    send({
        id: t.getAttribute("data-rask-on-click"), type: "click",
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    });
});

// rAF-coalesced input & scroll dispatch (inputPending/flushInputsNow/queueInput + the input and
// scroll listeners) — spliced from Rask.Core/Resources/rask-input.js, shared with rask.js +
// rask.js. MUST precede @@RASK_EVENTS@@ (its keyboard handler calls flushInputsNow).
// @@RASK_INPUT@@

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
        // What the frame reports, from the shared module (rask-morph.js) rather than computed here —
        // the hosts each carried their own copy and drifted, which is how <select> ended up with no
        // lagging-frame guard. `values` is null for everything except a <select multiple>, whose
        // `.value` is only its FIRST selected option.
        const changeVal = raskChangeFrameValue(t);
        const changeVals = raskChangeFrameValues(t);
        // Record what a lagging re-render would have to carry to be stale — the pre-edit value, the
        // pre-click checked (whole radio group), the pre-pick selected (whole select) — so the apply
        // paths can tell "the frame that predates the user's action" from "the server's authoritative
        // answer to it". Shared with the Server runtime, in rask-morph.js.
        raskNotePendingFormState(t);
        const changeFrame = {
            id: t.getAttribute("data-rask-on-change"), type: "change", value: changeVal
        };
        if (changeVals !== null) changeFrame.values = changeVals;
        send(changeFrame);
    }
});

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

// Extended GlobalEventHandlers delegation + keyboard (keydown/keyup) + the four core drag events
// (dragstart/dragover/drop/dragend) — spliced from Rask.Core/Resources/rask-events.js.
// @@RASK_EVENTS@@

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
        // CSS.escape the id so a value carrying a quote/bracket can't break out of the
        // attribute selector or match an unintended element (defense-in-depth — ids are
        // framework-minted, but the reviver runs on server-supplied JSON).
        if (typeof value.__raskRef__ === "string") {
            return document.querySelector(`[data-rask-ref="${CSS.escape(value.__raskRef__)}"]`);
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
    invokeMethodAsync(assemblyName, methodIdentifier, ...args) {
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
