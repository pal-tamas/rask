// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

// What this host needs from the shared modules. Each of these used to arrive by being pasted into
// the same scope at a `// @@RASK_*@@` marker, in an order the build had to get right and nothing
// checked; they are now an import list the compiler verifies.
//
// The framework's own browser shims (rask-api, browser/globals, rask-wasm-api) are imported for their side
// effects: each publishes a `window.__rask*` namespace that .NET reaches by dotted name, so there is
// nothing to bind here.
import { applyDiff, applyFrameInvokes, type DiffOp } from "../../Rask.Core/Resources/rask-dom.js";
import {
    closestFrom,
    morph,
    raskChangeFrameValue,
    raskChangeFrameValues,
    raskNotePendingFormState,
} from "../../Rask.Core/Resources/rask-morph.js";
import { changeSent, flushInputsNow } from "../../Rask.Core/Resources/rask-input.js";
import {
    preloadNewHeadStylesheets,
    waitForUnappliedHeadCss,
} from "../../Rask.Core/Resources/rask-scoped.js";
import { raskReadFileChunk, raskRegisterFiles } from "../../Rask.Core/Resources/rask-files.js";
import { beginUpload } from "../../Rask.Core/Resources/rask-upload.js";
import { showDevError } from "../../Rask.Core/Resources/rask-deverror.js";
import { showHotReloadPill } from "../../Rask.Core/Resources/rask-hotreload.js";
import { createInvokeGate } from "../../Rask.Core/Resources/rask-head-assets.js";
import { setHost } from "../../Rask.Core/Resources/rask-host.js";
import { eventBatch, isHandlerEvent } from "../../Rask.Core/Resources/rask-batch.js";
import {
    beginLoading,
    endLoading,
    isVisiblyLoading,
    loadingTarget,
} from "../../Rask.Core/Resources/rask-loading.js";

import "../../Rask.Core/Resources/rask-api.js";
import "../../Rask.Core/Resources/rask-events.js";
import { loadHooksOnDemand } from "../../Rask.Core/Resources/rask-hook-loader.js";
import { mayLeave } from "../../Rask.Core/Resources/rask-owned.js";
import { raskDomPayload } from "../../Rask.Core/Resources/rask-dom-payload.js";
import { handlerClick, inAppUrl, navLinkClick } from "../../Rask.Core/Resources/rask-clicks.js";
import {
    createJSObjectReference,
    disposeJSObjectReferenceById,
    invokeJs,
} from "../../Rask.Core/Resources/rask-js-invoke.js";
import "./rask-wasm-api.js";

let dotnetExports: RaskWasmExports | null = null;
let root: HTMLElement | null = null;
let basePath: string | null = null;

// Serializes render application across payloads. A navigation diff/full reply may defer
// its body swap until the new page's scoped CSS applies (waitForUnappliedHeadCss /
// preloadNewHeadStylesheets), opening a microtask/timer gap during which .NET could
// deliver the next render. Both
// the diff and full-HTML paths chain through this tail promise so a deferred body
// always commits before the following payload's ops — paths in a later diff are
// computed against the render this one produces, so they must not be applied first.
let _renderQueue = Promise.resolve();

// Island callbacks fired before the app took over. A prerendered page mounts its islands from real
// HTML, so they are clickable seconds before .NET has booted, and a callback dropped there is a click
// that did nothing. Held until the first frame has been applied — .NET has no session to dispatch
// into before that either — then delivered only to the island that fired it (see flushEarlyCallbacks).
const EARLY_CALLBACK_LIMIT = 32;
let earlyCallbacks: { payload: unknown; island: string }[] | null = [];

function isIslandCallback(payload: unknown): payload is { id: string } {
    const frame = payload as { type?: unknown; id?: unknown } | null;
    return !!frame && frame.type === "external" && typeof frame.id === "string";
}

/** The name of the island whose props carry this handler id, or null when none does. */
function islandHolding(id: string): string | null {
    // The id reaches a selector-free substring match, but only ever as h<digits>.
    if (!/^h\d+$/.test(id)) return null;
    for (const island of document.querySelectorAll("rask-external")) {
        if ((island.getAttribute("props") ?? "").includes(`"$h":"${id}"`)) return island.getAttribute("name");
    }
    return null;
}

function holdEarlyCallback(held: { payload: unknown; island: string }[], payload: { id: string }): void {
    const island = islandHolding(payload.id);
    if (island === null) {
        console.warn("[Rask] a callback fired before the app started names no island on the page; dropped.");
        return;
    }
    if (held.length === EARLY_CALLBACK_LIMIT) {
        console.warn(`[Rask] more than ${EARLY_CALLBACK_LIMIT} island callbacks fired before the app `
            + "started; the oldest was dropped.");
        held.shift();
    }
    held.push({payload, island});
}

// Handler ids are positional, and the prerendered page was rendered by another process: an id in it
// names the same handler only while the first live render matches. So a held callback is delivered
// only when an island of the SAME name still carries that id — anything else could fire a handler
// the visitor never touched.
function flushEarlyCallbacks(): void {
    const held = earlyCallbacks;
    earlyCallbacks = null;
    for (const {payload, island} of held ?? []) {
        if (islandHolding((payload as { id: string }).id) === island) {
            void send(payload);
        } else {
            console.warn(`[Rask] a callback the '${island}' island fired before the app started was `
                + "dropped: the page it was fired on is not the page the app rendered.");
        }
    }
}

// The "#fragment" of an intercepted nav-link click. The fragment never leaves the
// browser (the navigate message carries only path+query, and the history url has no
// hash), so we stash it here on click and consume it when the matching push reply
// commits — scroll to that anchor, else to the top. Cleared on consume.
let _pendingScrollHash = "";

// Parks a first-render Rask.X invoke until the head's assets have settled and window.Rask.X exists
// (rask-head-assets, shared with the server runtime).
const invokeGate = createInvokeGate<RaskPendingInvoke>(
    c => c.identifier,
    c => dispatchUnparked(c.taskId, c.identifier, c.argsJson, c.resultType, c.targetInstanceId));

// Read once from <base href> (or the page URL if no <base> is set) so the
// runtime can host under a sub-path like /Rask/ on GitHub Pages without the
// .NET side ever seeing the prefix. Resolves to the directory portion so a
// page URL like /index.html yields "/" (not "/index.html/").
export function getBasePath() {
    if (basePath !== null) return basePath;

    // Read the <base href> ELEMENT, not document.baseURI. With no <base> present, baseURI is the
    // current document's own URL, so a page at /realtime/BTC yields a bogus "/realtime/" base that
    // breaks every asset URL, the seeded route, and getBaseAddress below.
    //
    // A shell with <base href="/"> never hit this, since baseURI then equals that element's href; a
    // page served without one does. The Server runtime's own getBasePath carries the same fix.
    const baseEl = document.querySelector<HTMLBaseElement>("base[href]");
    if (!baseEl) {
        basePath = "/";
        return basePath;
    }

    const p = new URL(baseEl.href, location.href).pathname;
    const last = p.lastIndexOf("/");
    basePath = last < 0 ? "/" : p.slice(0, last + 1);
    return basePath;
}

function stripBase(pathname: string): string {
    const b = getBasePath();
    if (b === "/" || !pathname) return pathname;
    if (pathname === b.slice(0, -1) || pathname === b) return "/";
    return pathname.startsWith(b) ? "/" + pathname.slice(b.length) : pathname;
}

function prependBase(url: string): string {
    const b = getBasePath();
    if (b === "/" || typeof url !== "string" || !url.startsWith("/") || url.startsWith(b)) return url;
    return b + url.slice(1);
}

// Called from main.js once `getAssemblyExports` is available so the JS event
// handlers below can dispatch into .NET via the JSExport surface.
export function setExports(exports: RaskWasmExports): void {
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
    invokeGate.scanHeadAssets(true);

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

export function applyRender(payload: Uint8Array): void {
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
    // The byte count, not the view: `payload` is a transient MemoryView over the .NET write buffer.
    const devtools = window.__raskDevtoolsHook;
    if (devtools) devtools.recv(reply, payload.byteLength);
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
export function bootFailed(message: string, detail?: string): void {
    const report = window.__raskBootFailed;
    if (typeof report === "function") report(message, detail);
    else console.error(`[Rask] boot failed: ${message}`, detail ?? "");
}

// Dev-only. Called from .NET (WasmHotReloadBridge) once the hot-reload coordinator has finished
// applying an update and every open session has repainted — the WASM analogue of the Server's
// {"type":"hotReload","status":"applied"} frame. Purely an indicator: the DOM was already updated by
// the repaint that preceded this call, so it must not touch the tree.
//
// Calls the imported implementation directly rather than the `window.__raskHotReloadPill` global that
// rask-hotreload.ts also publishes (for the E2E fixture, which can only reach it from the page).
// Reading it off `window` here looked equivalent and was not: with nothing referencing the import,
// esbuild elided it, decided the module was unreachable, and dropped rask-hotreload.ts from the
// bundle entirely — side effect and all — so the global the guard tested was never assigned.
export function hotReloadApplied() {
    showHotReloadPill();
}

// The [JSImport] on the .NET side binds to this module export, so the export has to be declared here (an ES
// module cannot re-export a spliced-in function declaration by name from an inner scope). The registry and
// the read itself are shared.
export function readFileChunk(ref: string, offset: number, length: number): Promise<Uint8Array> {
    return raskReadFileChunk(ref, offset, length);
}

function registerFiles(inputEl: HTMLInputElement | null, files: FileList) {
    return raskRegisterFiles(inputEl, files);
}

function triggerDownload(download: RaskFrameReply["download"]): void {
    if (!download || typeof download.filename !== "string") return;
    let bytes;
    if (typeof download.token === "string" && download.token.length > 0
        && dotnetExports && dotnetExports.Rask && dotnetExports.Rask.Wasm
        && dotnetExports.Rask.Wasm.JSInterop
        && typeof dotnetExports.Rask.Wasm.JSInterop.PullDownload === "function") {
        // Token-pull path: bytes live in .NET, JSExport returns them directly as a Uint8Array.
        // No base64 inflation, no decode loop — render payload only carried the token string.
        bytes = dotnetExports.Rask.Wasm.JSInterop.PullDownload(download.token!);
    } else if (typeof download.base64 === "string") {
        // Legacy base64-inline path (test seam + back-compat).
        bytes = decodeBase64(download.base64);
    }
    if (!bytes || bytes.length === 0) return;
    const blob = new Blob([bytes as BlobPart], {type: download.contentType || "application/octet-stream"});
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = download.filename ?? "";
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
        try {
            document.body.removeChild(a);
        } catch {
            // Already detached — the click may have navigated away.
        }
        URL.revokeObjectURL(url);
    }, 0);
}

function decodeBase64(b64: string): Uint8Array {
    if (typeof b64 !== "string" || b64.length === 0) return new Uint8Array();
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

export function pushHistory(url: string, replace: boolean): void {
    const target = prependBase(url);
    if (replace) window.history.replaceState({rask: true}, "", target);
    else window.history.pushState({rask: true}, "", target);
}

// Go.Out: a page of this site the app does not render, loaded as a page. Its address is used as written, and only
// ever on this origin. The app sent the reader there, as a handler's Go.To does, so the unsaved-changes guard is
// not asked.
export function leaveTo(url: string): void {
    const target = new URL(url, location.origin);
    if (target.origin !== location.origin) return;
    // A form that no longer says it is guarded guards nothing (rask-leave.ts).
    document.querySelectorAll("form[data-rask-confirm-leave]").forEach(function (form) {
        form.removeAttribute("data-rask-confirm-leave");
    });
    location.assign(target.href);
}

function inRoot(el: Node | null): boolean {
    return !!root && !!el && root.contains(el);
}

function applyHistory(history: RaskFrameReply["history"]): void {
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
function applyNavScroll(history: RaskFrameReply["history"]): void {
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

function handle(reply: RaskFrameReply | null): void {
    if (!reply || typeof reply !== "object") return;
    // The app has painted. Set HERE, by the code that actually does it, because it is the only place
    // that knows: the morph patches the existing document in place rather than replacing it, so the
    // splash element main.js captured at import time is still connected afterwards and DOM state cannot
    // be read as "did we render". Inferring it from that element instead is what made every WASM journey
    // report a boot failure over an app that had rendered perfectly well.
    window.__raskPainted = true;
    // And the page is no longer merely prerendered. A prerendered document is served as real HTML, so
    // its controls are present and look clickable from the first paint — but no handler is attached
    // until the bundle downloads, starts and takes over, and anything clicked in that window is
    // silently lost. The window is short on a fast connection and seconds on a slow one, and it is
    // worst exactly where prerendering is most valuable: a first-time visitor on mobile data.
    //
    // `data-rask-prerendered` is on <html> from the moment the pass writes the page, so an app can
    // style the not-yet-live state — a cursor, a dimmed control, `inert` — with no script of its own,
    // and this is where that state ends. Cleared HERE for the same reason __raskPainted is set here:
    // it is the one place both render paths pass through. Doing it in the full-frame path alone would
    // look right and be wrong twice over — a diff-mode first frame never morphs, and the morph that
    // does happen removes the attribute as a side effect of replacing <html>'s attributes, which is
    // not the same as clearing it and would not survive an app that re-adds it. (#973)
    document.documentElement?.removeAttribute("data-rask-prerendered");
    // A development fault the app survived, riding the render payload (see the Server runtime for why
    // it is a field rather than a frame). Applied before either render path: the panel is a sibling of
    // the app, so it must not wait on the render queue.
    if (reply.devError) showDevError(reply.devError);
    // Diff-mode payload: apply ops directly against the live DOM. Both paths chain
    // through _renderQueue so a diff that defers its body for a CSS load can't be
    // overtaken by the next payload (see _renderQueue).
    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        _renderQueue = _renderQueue.then(
            () => { applyDiffReply(reply); },
            (e) => { reportQueuedRenderFailure(e); applyDiffReply(reply); });
    } else {
        _renderQueue = _renderQueue.then(
            () => { applyFullReply(reply); },
            (e) => { reportQueuedRenderFailure(e); applyFullReply(reply); });
    }
    // Behind the frame, so the ids a held callback is checked against are the live page's.
    if (earlyCallbacks) _renderQueue = _renderQueue.then(flushEarlyCallbacks, flushEarlyCallbacks);
}

// The render queue deliberately carries on after a failed frame — one bad payload must not wedge
// every later one — but the rejection used to be discarded, so a throw inside applyDiff or morph
// left no trace anywhere: no console error, no page error, nothing for a developer to go on. Report
// it and still apply the next frame.
function reportQueuedRenderFailure(error: unknown): void {
    console.error("[Rask] a queued render failed; the next frame was applied anyway", error);
}

// Per-invoke executor for the shared applyFrameInvokes loop (rask-dom.js). A frame's jsInvokes run
// AFTER applyDiff/morph patched the DOM — so a queued OnRenderedAsync focus acts on the committed
// DOM (e.g. a <dialog> that just gained its `open` attribute), the same post-commit ordering the
// Server has. beginInvokeJS runs the call and returns its result via the endInvokeJSResult JSExport.
function dispatchWasmInvoke(inv: RaskFrameJsInvoke): void {
    beginInvokeJS(
        String(inv.id),
        inv.identifier,
        typeof inv.argsJson === "string" ? inv.argsJson : null,
        typeof inv.resultType === "number" ? inv.resultType : 0,
        typeof inv.targetInstanceId === "number" ? String(inv.targetInstanceId) : "0");
}

function applyDiffReply(reply: RaskFrameReply): unknown {
    // The head isn't in the diff frame stream (user Head contributions are collected +
    // spliced render-side), so a head change rides the payload as a <head> fragment.
    // Morph it into document.head FIRST — keyed reconciliation (data-rask-key) keeps
    // unchanged scoped-CSS links, and morph skips data-rask-managed boot bundles so they
    // survive. When the new page adds a not-yet-cached scoped stylesheet, defer the body
    // ops until it loads so the swapped body never paints unstyled (FOUC).
    const applyBody = () => {
        const devtools = window.__raskDevtoolsHook;
        const startedAt = devtools ? performance.now() : 0;
        applyDiff((reply.ops ?? []) as DiffOp[], Array.isArray(reply.names) ? reply.names : undefined);
        if (devtools) devtools.commit(reply, startedAt);
        applyHistory(reply.history);
        applyNavScroll(reply.history);
        // A diff can insert Head-declared external <script>/<link> and scoped-JS tags
        // (keyed InsertSubtree). Track them so their load events feed the Rask.* invoke
        // gate, then drain anything now unblocked — the full-HTML morph path does the same.
        invokeGate.scanHeadAssets();
        invokeGate.drain();
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

function applyFullReply(reply: RaskFrameReply): unknown {
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
            const devtools = window.__raskDevtoolsHook;
            const startedAt = devtools ? performance.now() : 0;
            morph(document.documentElement, freshHtml);
            if (devtools) devtools.commit(reply, startedAt);
            root = document.querySelector("[data-rask-root]") || document.body;
            // Pick up any newly-inserted Head-declared external assets so
            // their load events feed into the Rask.* invoke gate.
            invokeGate.scanHeadAssets();
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

// The events one task produces cross into .NET as one call (rask-batch.ts), answered with a single render.
// Anything else goes at once, behind what was waiting, so nothing overtakes an earlier event.
const events = eventBatch(dispatch);

function send(payload: unknown): Promise<void> {
    if (earlyCallbacks && isIslandCallback(payload)) {
        holdEarlyCallback(earlyCallbacks, payload);
        return Promise.resolve();
    }
    if (isHandlerEvent(payload)) {
        // An event from before .NET exists is dropped NOW, not held for a task in which it might arrive:
        // its id is the prerendered page's, and the live page's may name another handler (#973).
        return dotnetExports ? events.add(payload) : dispatch(payload);
    }
    events.flush();
    return dispatch(payload);
}

async function dispatch(payload: unknown): Promise<void> {
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
        const devtools = window.__raskDevtoolsHook;
        if (devtools) devtools.send(payload, requestBytes.length);
        await dotnetExports!.Rask!.Wasm!.JSInterop!.Dispatch!(requestBytes);
    } catch (e) {
        console.error("Rask: dispatch failed", e);
    }
}

// Install the host contract before anything can dispatch. Both are hoisted function declarations,
// so this runs before the listeners below are bound.
//
// `send` answers with a promise where the Server host's answers with nothing: the promise is this host
// saying when .NET has handled the event and its render is on the page (rask-host.ts).
//
// Not optional and not merely tidy: the shared modules call send/inRoot through this indirection,
// and rask-host.ts's default throws rather than silently dropping events. Leaving the import
// unreferenced would additionally let esbuild elide rask-host.ts from the bundle.
setHost({send, inRoot});

// The behaviour hooks are a script of their own beside this module, loaded when the page first asks for one
// (rask-hook-loader.ts).
//
// A module that was not loaded from a place (a `data:` URL, which is how the Node fixtures import this bundle)
// has no "beside": there is nothing to load the hooks from, and nothing is watched for.
function hooksUrl(): string | null {
    try {
        return new URL("./rask-hooks.js", import.meta.url).href;
    } catch (e) {
        return null;
    }
}

const hooks = hooksUrl();
if (hooks) {
    loadHooksOnDemand(hooks);
}

// And the same two facts again, on a global, for modules that are NOT in this bundle.
//
// setHost above is an intra-bundle contract: a shared module imports `send` from rask-host.ts and
// the bundler resolves it. Rask.External is a separately published package, fetched by the browser
// from its own URL as its own module graph, so it has no import path to reach that binding at all.
//
// It must not open a channel of its own either. On this host that would mean an HTTP round trip to
// a server that may not exist, when the handler it wants is already in this tab's .NET runtime.
// Going through this bridge keeps an external component's callback a direct JSExport call, exactly
// like a DOM handler's.
globalThis.__raskHost = globalThis.__raskHost || {};
globalThis.__raskHost.send = send;
globalThis.__raskHost.navigate = (href: string, replace?: boolean) => {
    const url = inAppUrl(href);
    if (url) navigate(url, replace === true);
    else console.error(`[Rask] navigate: "${href}" is not a URL of this app, so nothing navigated.`);
};

document.addEventListener("click", (e) => {
    const url = navLinkClick(e);
    if (url) navigate(url, false);
});

// One in-app navigation, whoever asked: a click on a nav link, or front-end code through the bridge.
function navigate(url: URL, replace: boolean): void {
    // A form with unsaved edits (ConfirmLeave): the reader stays, or is being asked and this runs again.
    if (!mayLeave(() => navigate(url, replace))) return;
    // Stash the "#fragment" so applyNavScroll can scroll to the anchor once the new page commits
    // (the fragment is not sent to .NET).
    _pendingScrollHash = url.hash || "";
    flushInputsNow();
    send(replace
        ? {type: "navigate", path: stripBase(url.pathname), query: url.search, replace: true}
        : {type: "navigate", path: stripBase(url.pathname), query: url.search});
}

window.addEventListener("popstate", () => {
    flushInputsNow();
    send({type: "navigate", path: stripBase(location.pathname), query: location.search, replace: true});
});

document.addEventListener("click", (e) => {
    const t = handlerClick(e);
    if (!t) return;
    const waiting = loadingTarget(t);
    flushInputsNow();
    // The dispatch promise resolves once the handler AND its render are done, which is exactly how long
    // the control has been waiting.
    const ticket = waiting ? beginLoading(waiting) : null;
    // The click's PointerEvent, in MDN's fields (rask-dom-payload.ts).
    send(Object.assign(raskDomPayload(e, "click"), { id: t.getAttribute("data-rask-on-click"), type: "click" }))
        .finally(() => endLoading(ticket));
});

document.addEventListener("change", (e) => {
    const t = closestFrom(e.target, "[data-rask-on-change], [data-rask-on-files]");
    if (!t || !inRoot(t)) return;
    // Flush before processing — if the same element (or a sibling) has a pending
    // coalesced input, the server needs to see it BEFORE the change-triggered
    // validator / handler runs, otherwise the validator reads stale model state.
    flushInputsNow();
    const asInput = t.tagName === "INPUT" ? t as HTMLInputElement : null;
    if (asInput && asInput.type === "file" && asInput.hasAttribute("data-rask-on-files")) {
        const files = asInput.files;
        if (!files || files.length === 0) return;
        const metas = registerFiles(asInput, files);
        // Marked until the handler and its render are done; what it has read of the files is its progress.
        const upload = beginUpload(asInput);
        send({id: t.getAttribute("data-rask-on-files"), type: "files", files: metas}).finally(upload.end);
        return;
    }
    if (t.hasAttribute("data-rask-on-change") && !changeSent(t)) {
        // What the frame reports, from the shared module (rask-morph.js) rather than computed here —
        // the hosts each carried their own copy and drifted, which is how <select> ended up with no
        // lagging-frame guard. `values` is null for everything except a <select multiple>, whose
        // `.value` is only its FIRST selected option.
        // The three tags a change frame can come from; raskChangeFrameValue reads `.value` and
        // `.checked`, neither of which is on the base HTMLElement.
        const field = t as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
        const changeVal = raskChangeFrameValue(field);
        const changeVals = raskChangeFrameValues(field);
        // Record what a lagging re-render would have to carry to be stale — the pre-edit value, the
        // pre-click checked (whole radio group), the pre-pick selected (whole select) — so the apply
        // paths can tell "the frame that predates the user's action" from "the server's authoritative
        // answer to it". Shared with the Server runtime, in rask-morph.js.
        raskNotePendingFormState(t);
        const changeFrame: {
            id: string | null;
            type: string;
            value: string;
            values?: string[];
        } = {
            id: t.getAttribute("data-rask-on-change"), type: "change", value: changeVal
        };
        if (changeVals !== null) changeFrame.values = changeVals;
        send(changeFrame);
    }
});

document.addEventListener("submit", (e) => {
    const t = closestFrom(e.target, "[data-rask-on-submit]");
    if (!t || !inRoot(t)) return;
    e.preventDefault();
    // The form's own button waits, not the form. Mirrors rask.ts.
    const submitter = loadingTarget((e as SubmitEvent).submitter ?? null);
    if (isVisiblyLoading(submitter)) return;
    flushInputsNow();
    const fileInputs = t.querySelectorAll<HTMLInputElement>('input[type="file"][name]');
    const fileFields: Record<string, unknown> = {};
    for (const input of fileInputs) {
        if (!input.files || input.files.length === 0) continue;
        fileFields[input.name] = registerFiles(input, input.files);
    }

    // A submit handler is only ever bound to a <form>, which is what makes FormData legal here.
    const fd = new FormData(t as HTMLFormElement);
    const obj: Record<string, unknown> = {};
    fd.forEach((v, k) => {
        // File parts are carried by fileFields above, under a ref rather than inline.
        if (typeof v !== "string") return;
        obj[k] = v;
    });
    if (Object.keys(fileFields).length > 0) obj.__files = fileFields;
    const ticket = submitter ? beginLoading(submitter) : null;
    send({id: t.getAttribute("data-rask-on-submit"), type: "submit", form: obj})
        .finally(() => endLoading(ticket));
});

// ----- IJSRuntime bridge -----------------------------------------------------
// Called by Rask.Wasm.JSInterop.BeginInvokeJSImport (a [JSImport]). The dispatcher itself is shared
// with the Server host (rask-js-invoke.ts); this host parks an invoke behind the head-asset gate and
// ships its outcome back through the EndInvokeJSResult JSExport.

function endInvokeJSResult(taskId: string, success: boolean, result: unknown, error?: string): void {
    if (!dotnetExports || !dotnetExports.Rask || !dotnetExports.Rask.Wasm
        || !dotnetExports.Rask.Wasm.JSInterop) return;
    const payload = success
        ? [Number(taskId), true, (result === undefined ? null : result)]
        : [Number(taskId), false, error || "JS invocation failed"];
    try {
        dotnetExports!.Rask!.Wasm!.JSInterop!.EndInvokeJSResult!(JSON.stringify(payload));
    } catch (e) {
        console.error("[Rask] EndInvokeJSResult failed", e);
    }
}

export function beginInvokeJS(
    taskId: string,
    identifier: string,
    argsJson: string | null,
    resultType: number,
    targetInstanceId: string): void {
    // A Rask.* call waits while head assets are still loading or window.Rask.{TypeName} does not exist yet
    // (a first-render OnRenderedAsync racing its scoped script); the gate dispatches it when both clear.
    if (invokeGate.park({taskId, identifier, argsJson, resultType, targetInstanceId})) return;
    dispatchUnparked(taskId, identifier, argsJson, resultType, targetInstanceId);
}

function dispatchUnparked(
    taskId: string,
    identifier: string,
    argsJson: string | null,
    resultType: number,
    targetInstanceId: string): void {
    invokeJs(identifier, argsJson, resultType, Number(targetInstanceId),
        (success, result, error) => endInvokeJSResult(taskId, success, result, error));
}

// ----- DotNet shim (mirror of Blazor's window.DotNet, for [JSInvokable]) -----
const dotNetPending = new Map<string, { resolve: (v: never) => void; reject: (e: Error) => void }>();
let nextDotNetCallId = 1;

window.DotNet = window.DotNet || {
    invokeMethodAsync<T = unknown>(assemblyName: string, methodIdentifier: string, ...args: unknown[]): Promise<T> {
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
    },
    disposeJSObjectReferenceById,
    // A live object handed to .NET as an IJSObjectReference (a Rask.Web event's device), held until disposed of.
    createJSObjectReference
};

export function endDotNetInvoke(resultJson: string): void {
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
    if (msg.success) pending.resolve(msg.result as never);
    else pending.reject(new Error(msg.error || "DotNet invocation failed"));
}
