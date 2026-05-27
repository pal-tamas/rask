// Node-driven fixture for the Rask.Wasm head-asset / scoped-JS invoke gate.
//
// Goal: at the JS layer, reproduce the user-reported "undefined is not an
// object (evaluating 'window.hljs.highlightElement')" crash. The crash
// happens when an external <script> contributed via a Component's Head
// (in the user's case CodeSample.Head -> highlight.min.js) terminates
// without successfully defining the global the queued Rask.* invoke
// depends on — either via an 'error' event (CDN flake, blocked by an
// extension, integrity mismatch on refresh, CSP) OR via the gate's 5s
// safety timeout (slow network). Both terminal paths run the same
// `done()` closure today, which drains every queued Rask.* invoke. The
// queued user code then dereferences the still-undefined global and
// throws — surfacing in .NET as a JSException, faulting OnRenderedAsync,
// and tripping the RootErrorBoundary.
//
// This fixture imports the production rask.wasm.js bundle into a minimal
// stub DOM, then drives the public surface (setExports + applyRender +
// beginInvokeJS) to:
//   * open the scoped-JS half of the gate (applyScopedJs)
//   * register a Head-declared <script> in stub-document.head
//   * park a Rask.* invoke that depends on the script's global
//   * fire 'error' on the script
//   * observe whether the queued invoke ran anyway
//
// The C# test (HeadAssetErrorDrainsQueueTests) consumes the single JSON
// line on stdout and asserts the recorded values match the bug pattern.
// When the bug is fixed (gate stops draining on 'error'/timeout), the
// expectations flip and the test reads as a regression guard.
import { readFileSync } from "node:fs";

const bundlePath = process.argv[2];
if (!bundlePath) {
    console.error("usage: node HeadAssetGateFixture.mjs <rask.wasm.js path>");
    process.exit(2);
}

// ----- Minimal browser stubs -----
function makeStubElement(tagName) {
    const attrs = new Map();
    const listeners = new Map();
    const el = {
        nodeType: 1,
        tagName: tagName.toUpperCase(),
        _children: [],
        parentNode: null,
        textContent: "",
        get rel() { return attrs.get("rel") ?? ""; },
        get src() { return attrs.get("src") ?? ""; },
        get href() { return attrs.get("href") ?? ""; },
        get id() { return attrs.get("id") ?? ""; },
        set id(v) { attrs.set("id", String(v)); },
        get text() { return el.textContent; },
        set text(v) { el.textContent = String(v); },
        hasAttribute: (name) => attrs.has(name),
        getAttribute: (name) => (attrs.has(name) ? attrs.get(name) : null),
        setAttribute: (name, value) => attrs.set(name, String(value)),
        removeAttribute: (name) => attrs.delete(name),
        addEventListener: (type, fn) => {
            if (!listeners.has(type)) listeners.set(type, []);
            listeners.get(type).push(fn);
        },
        _dispatch: (type) => {
            const fns = listeners.get(type) || [];
            for (const fn of fns.slice()) fn({ type, target: el });
        },
        appendChild: (child) => {
            el._children.push(child);
            child.parentNode = el;
            return child;
        },
        get attributes() {
            return [...attrs.entries()].map(([name, value]) => ({ name, value }));
        }
    };
    return el;
}

function selectorMatches(el, selector) {
    if (selector === "script[src]") {
        return el.tagName === "SCRIPT" && el.hasAttribute("src");
    }
    if (selector === "link[rel=stylesheet]") {
        return el.tagName === "LINK" && el.getAttribute("rel") === "stylesheet";
    }
    return false;
}

const head = makeStubElement("head");
head.querySelectorAll = (selector) => {
    const parts = selector.split(",").map(s => s.trim());
    return head._children.filter(c => parts.some(p => selectorMatches(c, p)));
};

const body = makeStubElement("body");
body.contains = () => false;
body.setAttribute("data-rask-root", "");

globalThis.document = {
    head,
    body,
    documentElement: { tagName: "HTML" },
    getElementById: (id) => head._children.find(c => c.getAttribute("id") === id) || null,
    createElement: (tag) => makeStubElement(tag),
    querySelector: (sel) => {
        if (sel === "[data-rask-root]") return body;
        return null;
    },
    addEventListener: () => {}
};
globalThis.window = globalThis;
// The bundle wires top-level `window.addEventListener("popstate", ...)` and a
// few `document.addEventListener(...)` handlers at module init. Provide
// no-op stubs so the import doesn't fault — the gate scenarios under test
// never dispatch any of these events.
globalThis.addEventListener = () => {};
globalThis.performance = { getEntriesByName: () => [] };
globalThis.location = { pathname: "/", search: "" };
globalThis.history = { replaceState: () => {}, pushState: () => {} };
globalThis.cancelAnimationFrame = () => {};
globalThis.requestAnimationFrame = () => 0;
// Node already exposes a (getter-only) `crypto` global on v19+; skip the
// override if one is present so the assignment doesn't fault here.
if (typeof globalThis.crypto === "undefined") {
    globalThis.crypto = { randomUUID: () => "stub-uuid" };
}

// Capture the gate's 5-second safety timeout so the test can fire it on
// demand instead of waiting 5 real seconds.
const capturedSafetyTimeouts = [];
const realSetTimeout = globalThis.setTimeout;
globalThis.setTimeout = (fn, delay) => {
    if (delay >= 1000) {
        capturedSafetyTimeouts.push(fn);
        return capturedSafetyTimeouts.length;
    }
    return realSetTimeout(fn, delay);
};

// ----- Load the bundle -----
const bundleSource = readFileSync(bundlePath, "utf8");
const moduleUrl = "data:text/javascript;base64," + Buffer.from(bundleSource).toString("base64");
const mod = await import(moduleUrl);

// ----- Helpers -----
const encoder = new TextEncoder();
function applyRenderJson(reply) {
    mod.applyRender(encoder.encode(JSON.stringify(reply)));
}

function flushMicrotasks() {
    return new Promise(resolve => realSetTimeout(resolve, 0));
}

// ----- Scenario: 'error' event on a Head-declared script -----
// 1. setExports() runs scanHeadAssets on a head with one script element.
// 2. applyScopedJs (via an html-less applyRender) opens the scoped-JS gate.
// 3. beginInvokeJS parks a Rask.* invoke (script is still 'pending').
// 4. We fire 'error' on the script.
// 5. Observe whether the queued invoke ran while window.hljs is undefined.

// Pre-populate head with the script BEFORE setExports so the initial scan
// picks it up — same shape as production (App+CodeSample Head contributions
// land in document.head via the runtime's morph, then scanHeadAssets runs).
const hljsScript = makeStubElement("script");
hljsScript.setAttribute("src", "https://cdn.jsdelivr.net/.../highlight.min.js");
head.appendChild(hljsScript);

mod.setExports({
    Rask: {
        Wasm: {
            JSInterop: {
                Dispatch: () => {},
                // Production wires EndInvokeJSResult to a [JSExport]. The gate
                // calls it after running a queued invoke; without the stub the
                // bundle logs a noisy error to stderr.
                EndInvokeJSResult: () => {}
            }
        }
    }
});

// Open the scoped-JS half of the gate. Without html the runtime skips
// morph + the post-morph scanHeadAssets re-run — only applyScopedJs fires.
applyRenderJson({ jsHash: "test-hash", jsText: "// scoped-js bundle stub" });

// Capture console.warn so we can assert the gate emits a diagnostic when
// a Head asset terminates without successfully defining its global.
const capturedWarnings = [];
const realConsoleWarn = console.warn;
console.warn = (...args) => {
    capturedWarnings.push(args.map(String).join(" "));
    // Suppress in test output to keep stderr clean.
};

// Stub the user's exported function. We model the production CodeSample
// shape: a defensive guard against an undefined dep, then the actual
// hljs call. With the fix landed, dereferencing the dep happens only
// when it's defined — undefined-deref no longer surfaces as an
// uncaught TypeError.
let invokeFired = false;
let invokeSawHljs = null;
let invokeThrew = null;
globalThis.Rask = {
    CodeSample: {
        rendered: function () {
            invokeFired = true;
            invokeSawHljs = typeof globalThis.hljs !== "undefined";
            try {
                // Mirrors the post-fix CodeSample.js: guard, then call. The
                // gate's drain is correct (queued invokes must eventually run
                // so the page doesn't hang); the *user code* is what makes
                // the page survive a failed asset.
                if (typeof globalThis.hljs === "undefined"
                    || typeof globalThis.hljs.highlightElement !== "function") {
                    return;
                }
                globalThis.hljs.highlightElement({});
            } catch (e) {
                invokeThrew = e.message;
            }
        }
    }
};

// Park the invoke. resultType=3 is JSCallResultType.JSVoidResult — matches
// the CodeSample.OnRenderedAsync InvokeVoidAsync call.
mod.beginInvokeJS("1", "Rask.CodeSample.rendered", "[true]", 3, "0");

await flushMicrotasks();
const firedBeforeError = invokeFired;

// Now fire 'error' on the script — mimics CDN failure / refresh cache miss
// / extension block. window.hljs is NEVER defined as a side effect.
hljsScript._dispatch("error");

await flushMicrotasks();
const firedAfterError = invokeFired;

// Single JSON line for the C# test to parse.
process.stdout.write(JSON.stringify({
    firedBeforeError,         // false — gate held the invoke while asset pending
    firedAfterError,          // true  — gate drained on error (so page doesn't hang)
    invokeSawHljs,            // false — at invoke time, the failed asset's global is still undefined
    invokeThrew,              // null  — defensive user code degrades gracefully, no throw
    safetyTimeoutScheduled: capturedSafetyTimeouts.length >= 1,
    // Diagnostic emitted by the gate's 'error' path — must name the URL so
    // the developer can trace the consequent un-highlighted DOM back to the
    // asset that failed.
    warnedAboutFailedAsset: capturedWarnings.some(w =>
        w.includes("[Rask]") && w.includes("highlight.min.js") && w.includes("error"))
}) + "\n");
