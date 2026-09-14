// Node-driven fixture: does a scoped-JS <script> that arrived IN THE SERVED DOCUMENT hold Rask.* invokes?
//
// A prerendered page ships its <script data-rask-key="rsk-js" defer> in <head>. The parser runs it before
// main.js imports the runtime, so its load event has fired long before the runtime could listen for one.
// The invoke gate waited for that event anyway and parked every Rask.* call until the 30s backstop — on the
// site, "Measure the box" and every CodeSample's highlight and copy did nothing for half a minute after a
// refresh. A scoped script the MORPH inserts later is a different case, deliberately left waiting for its
// load event: its namespace does not exist until it has run.
//
// Drives the built rask.wasm.js against a minimal stub DOM and prints one JSON line.
import {readFileSync} from "node:fs";

const bundlePath = process.argv[2];
if (!bundlePath) {
    console.error("usage: node PrerenderedScopedJsGateFixture.mjs <rask.wasm.js path>");
    process.exit(2);
}

interface StubElement {
    nodeType: number;
    tagName: string;
    _children: StubElement[];
    readonly src: string;
    hasAttribute(name: string): boolean;
    getAttribute(name: string): string | null;
    setAttribute(name: string, value: string): void;
    removeAttribute(name: string): void;
    addEventListener(type: string, fn: (event: unknown) => void): void;
    _dispatch(type: string): void;
    appendChild(child: StubElement): StubElement;
    querySelectorAll?: (selector: string) => StubElement[];
    contains?: (node: unknown) => boolean;
}

function makeStubElement(tagName: string): StubElement {
    const attrs = new Map<string, string>();
    const listeners = new Map<string, ((event: unknown) => void)[]>();
    const el: StubElement = {
        nodeType: 1,
        tagName: tagName.toUpperCase(),
        _children: [],
        get src() {
            return attrs.get("src") ?? "";
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
            for (const fn of (listeners.get(type) || []).slice()) fn({type, target: el});
        },
        appendChild: (child: StubElement) => {
            el._children.push(child);
            return child;
        },
    };
    return el;
}

const head = makeStubElement("head");
head.querySelectorAll = (selector: string) =>
    head._children.filter(c => selector.split(",").some(p => p.trim() === "script[src]"
        ? c.tagName === "SCRIPT" && c.hasAttribute("src")
        : false));

const body = makeStubElement("body");
body.contains = () => false;
body.setAttribute("data-rask-root", "");

const globals = globalThis as unknown as Record<string, unknown>;
globals.document = {
    head,
    body,
    documentElement: {tagName: "HTML", removeAttribute: () => { }},
    getElementById: () => null,
    createElement: (tag: string) => makeStubElement(tag),
    querySelector: (sel: string) => sel === "[data-rask-root]" ? body : null,
    addEventListener: () => { },
};
globals.window = globalThis;
globals.addEventListener = () => { };
globals.performance = {getEntriesByName: () => []};
globals.location = {pathname: "/", search: "", origin: "http://localhost"};
globals.history = {replaceState: () => { }, pushState: () => { }};
globals.cancelAnimationFrame = () => { };
globals.requestAnimationFrame = () => 0;
if (typeof globals.crypto === "undefined") {
    globals.crypto = {randomUUID: () => "stub-uuid"};
}

// The gate's backstops (5s / 30s) and the namespace poll are captured, never run: an invoke that only
// fires because a backstop expired is exactly the defect, so letting one expire would hide it.
const realSetTimeout = globals.setTimeout as (fn: () => void, delay?: number) => number;
globals.setTimeout = (fn: () => void, delay?: number) => (delay ?? 0) >= 100 ? 0 : realSetTimeout(fn, delay);
globals.setInterval = () => 0;

const bundleSource = readFileSync(bundlePath, "utf8");
const moduleUrl = "data:text/javascript;base64," + btoa(unescape(encodeURIComponent(bundleSource)));
const mod = await import(moduleUrl) as {
    setExports(exports: unknown): void;
    beginInvokeJS(taskId: string, identifier: string, argsJson: string | null, resultType: number,
                  targetInstanceId: string): void;
};

const flush = (): Promise<void> => new Promise(resolve => realSetTimeout(() => resolve(), 0));

// The served document's scoped bundle, already executed: it has registered its namespace.
const served = makeStubElement("script");
served.setAttribute("src", "http://localhost/_rask/a/0123456789ab.js");
served.setAttribute("data-rask-key", "rsk-js");
head.appendChild(served);

const fired: string[] = [];
const scoped = globalThis as unknown as { Rask: Record<string, Record<string, () => void>> };
scoped.Rask = {Measure: {width: () => void fired.push("Measure.width")}};

mod.setExports({Rask: {Wasm: {JSInterop: {Dispatch: () => { }, EndInvokeJSResult: () => { }}}}});

mod.beginInvokeJS("1", "Rask.Measure.width", "[]", 3, "0");
await flush();
const servedScriptHeldTheInvoke = !fired.includes("Measure.width");

process.stdout.write(JSON.stringify({servedScriptHeldTheInvoke}) + "\n");
