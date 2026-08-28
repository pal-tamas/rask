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
import {readFileSync} from "node:fs";

const bundlePath = process.argv[2];
if (!bundlePath) {
    console.error("usage: node HeadAssetGateFixture.mjs <rask.wasm.js path>");
    process.exit(2);
}

// ----- Minimal browser stubs -----
//
// Deliberately a stub rather than jsdom: the gate is about the ORDER in which a Head-declared asset
// finishes loading relative to a Rask.* invoke, and that ordering is what the fixture drives by
// dispatching load/error itself. A real browser would decide it.
interface StubElement {
    nodeType: number;
    tagName: string;
    _children: StubElement[];
    parentNode: StubElement | null;
    textContent: string;
    readonly rel: string;
    readonly src: string;
    readonly href: string;
    id: string;
    text: string;
    hasAttribute(name: string): boolean;
    getAttribute(name: string): string | null;
    setAttribute(name: string, value: string): void;
    removeAttribute(name: string): void;
    addEventListener(type: string, fn: (event: unknown) => void): void;

    /** Fires a load/error the fixture controls the timing of; the whole point of the stub. */
    _dispatch(type: string): void;

    appendChild(child: StubElement): StubElement;
    readonly attributes: { name: string; value: string }[];

    /** Only the head carries one, assigned below. */
    querySelectorAll?: (selector: string) => StubElement[];

    /** Only the render root is asked this, by the event router's in-root check. */
    contains?: (node: unknown) => boolean;
}

function makeStubElement(tagName: string): StubElement {
    const attrs = new Map<string, string>();
    const listeners = new Map<string, ((event: unknown) => void)[]>();
    const el: StubElement = {
        nodeType: 1,
        tagName: tagName.toUpperCase(),
        _children: [],
        parentNode: null,
        textContent: "",
        get rel() {
            return attrs.get("rel") ?? "";
        },
        get src() {
            return attrs.get("src") ?? "";
        },
        get href() {
            return attrs.get("href") ?? "";
        },
        get id() {
            return attrs.get("id") ?? "";
        },
        set id(v: string) {
            attrs.set("id", String(v));
        },
        get text() {
            return el.textContent;
        },
        set text(v: string) {
            el.textContent = String(v);
        },
        hasAttribute: (name: string) => attrs.has(name),
        getAttribute: (name: string) => attrs.get(name) ?? null,
        setAttribute: (name: string, value: string) => void attrs.set(name, String(value)),
        removeAttribute: (name: string) => void attrs.delete(name),
        addEventListener: (type: string, fn: (event: unknown) => void) => {
            if (!listeners.has(type)) listeners.set(type, []);
            listeners.get(type)!.push(fn);
        },
        _dispatch: (type: string) => {
            const fns = listeners.get(type) || [];
            for (const fn of fns.slice()) fn({type, target: el});
        },
        appendChild: (child: StubElement) => {
            el._children.push(child);
            child.parentNode = el;
            return child;
        },
        get attributes() {
            return [...attrs.entries()].map(([name, value]) => ({name, value}));
        }
    };
    return el;
}

function selectorMatches(el: StubElement, selector: string): boolean {
    if (selector === "script[src]") {
        return el.tagName === "SCRIPT" && el.hasAttribute("src");
    }
    if (selector === "link[rel=stylesheet]") {
        return el.tagName === "LINK" && el.getAttribute("rel") === "stylesheet";
    }
    return false;
}

const head = makeStubElement("head");
head.querySelectorAll = (selector: string) => {
    const parts = selector.split(",").map(part => part.trim());
    return head._children.filter(c => parts.some(p => selectorMatches(c, p)));
};

const body = makeStubElement("body");
body.contains = () => false;
body.setAttribute("data-rask-root", "");

/**
 * The browser globals the bundle reaches for, none of which Node has.
 *
 * One `unknown`-typed view rather than a cast at each assignment: every one of these is a partial
 * fake, and saying so once keeps the assignments below reading as the list of things the bundle
 * needs — which is what a reader of this fixture is here for.
 */
const globals = globalThis as unknown as {
    document: unknown;
    window: unknown;
    addEventListener: () => void;
    performance: unknown;
    location: unknown;
    history: unknown;
    cancelAnimationFrame: () => void;
    requestAnimationFrame: () => number;
    crypto?: unknown;
    setTimeout: (fn: () => void, delay?: number) => number;
    Rask?: Record<string, Record<string, (...args: never[]) => unknown>>;
};

globals.document = {
    head,
    body,
    documentElement: {tagName: "HTML"},
    getElementById: (id: string) => head._children.find(c => c.getAttribute("id") === id) || null,
    createElement: (tag: string) => makeStubElement(tag),
    querySelector: (sel: string) => {
        if (sel === "[data-rask-root]") return body;
        return null;
    },
    addEventListener: () => {
    }
};
globals.window = globalThis;
// The bundle wires top-level `window.addEventListener("popstate", ...)` and a
// few `document.addEventListener(...)` handlers at module init. Provide
// no-op stubs so the import doesn't fault — the gate scenarios under test
// never dispatch any of these events.
globals.addEventListener = () => {
};
globals.performance = {getEntriesByName: () => []};
globals.location = {pathname: "/", search: ""};
globals.history = {
    replaceState: () => {
    }, pushState: () => {
    }
};
globals.cancelAnimationFrame = () => {
};
globals.requestAnimationFrame = () => 0;
// Node already exposes a (getter-only) `crypto` global on v19+; skip the
// override if one is present so the assignment doesn't fault here.
if (typeof globals.crypto === "undefined") {
    globals.crypto = {randomUUID: () => "stub-uuid"};
}

// Capture the gate's 5-second safety timeout so the test can fire it on
// demand instead of waiting 5 real seconds.
const capturedSafetyTimeouts: (() => void)[] = [];
const realSetTimeout = globals.setTimeout;
globals.setTimeout = (fn: () => void, delay?: number) => {
    if ((delay ?? 0) >= 1000) {
        capturedSafetyTimeouts.push(fn);
        return capturedSafetyTimeouts.length;
    }
    return realSetTimeout(fn, delay);
};

// ----- Load the bundle -----
const bundleSource = readFileSync(bundlePath, "utf8");
// btoa rather than Buffer: it is standard, so the fixture needs no further Node declarations.
const moduleUrl = "data:text/javascript;base64," + btoa(unescape(encodeURIComponent(bundleSource)));
const mod = await import(moduleUrl) as {
    applyRender(bytes: Uint8Array): void;
    setExports(exports: unknown): void;
    // Mirrors the export in rask.wasm.ts. taskId and targetInstanceId are STRINGS: they cross the
    // JSExport boundary as .NET longs, which JS cannot hold exactly.
    beginInvokeJS(taskId: string, identifier: string, argsJson: string | null, resultType: number,
                  targetInstanceId: string): void;
};

// ----- Helpers -----
const encoder = new TextEncoder();

function applyRenderJson(reply: unknown) {
    mod.applyRender(encoder.encode(JSON.stringify(reply)));
}

function flushMicrotasks(): Promise<void> {
    return new Promise<void>(resolve => realSetTimeout(() => resolve(), 0));
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
                Dispatch: () => {
                },
                // Production wires EndInvokeJSResult to a [JSExport]. The gate
                // calls it after running a queued invoke; without the stub the
                // bundle logs a noisy error to stderr.
                EndInvokeJSResult: () => {
                }
            }
        }
    }
});

// Open the scoped-JS half of the gate. Without html the runtime skips
// morph + the post-morph scanHeadAssets re-run — only applyScopedJs fires.
applyRenderJson({jsHash: "test-hash", jsText: "// scoped-js bundle stub"});

// Capture console.warn so we can assert the gate emits a diagnostic when
// a Head asset terminates without successfully defining its global.
// The gate warns when a Head asset terminates without defining its global; the diagnostic is
// part of the contract, so it is captured and asserted rather than merely suppressed.
const capturedWarnings: string[] = [];
console.warn = (...args: unknown[]) => {
    capturedWarnings.push(args.map(String).join(" "));
    // Suppress in test output to keep stderr clean.
};

// Stub the user's exported function. We model the production CodeSample
// shape: a defensive guard against an undefined dep, then the actual
// hljs call. With the fix landed, dereferencing the dep happens only
// when it's defined — undefined-deref no longer surfaces as an
// uncaught TypeError.
let invokeFired = false;
let invokeSawHljs: boolean | null = null;
let invokeThrew: string | null = null;
// The scoped-asset namespace the gate parks Rask.* invokes against, standing in for what a
// component's own compiled asset would register.
const scoped = globalThis as unknown as {
    Rask: Record<string, Record<string, () => void>>;
    hljs?: { highlightElement(el: unknown): void };
};
scoped.Rask = {
    CodeSample: {
        rendered: function () {
            invokeFired = true;
            invokeSawHljs = typeof scoped.hljs !== "undefined";
            try {
                // Mirrors the post-fix CodeSample.js: guard, then call. The
                // gate's drain is correct (queued invokes must eventually run
                // so the page doesn't hang); the *user code* is what makes
                // the page survive a failed asset.
                if (typeof scoped.hljs === "undefined"
                    || typeof scoped.hljs.highlightElement !== "function") {
                    return;
                }
                scoped.hljs.highlightElement({});
            } catch (e) {
                invokeThrew = e instanceof Error ? e.message : String(e);
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
    warnedAboutFailedAsset: capturedWarnings.some((w: string) =>
        w.includes("[Rask]") && w.includes("highlight.min.js") && w.includes("error"))
}) + "\n");
