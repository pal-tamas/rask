// Node-driven fixture: what happens to an island callback fired BEFORE the WASM app has booted?
//
// A prerendered page mounts its islands from the served HTML, so a visitor can press one seconds
// before .NET exists. The runtime used to drop that callback with a console warning. It now holds it
// until the first frame is applied and delivers it — but only to the island that fired it, because
// handler ids are positional and the prerendered page was rendered by another process.
//
// Drives the built rask.wasm.js against a minimal stub DOM and prints one JSON line.
//
// Usage:  node EarlyIslandCallbackFixture.mjs <rask.wasm.js path> <scenario>
//   delivered      one callback before boot; the live page still has that island.
//   other-island   the id belongs to a different island once the app has rendered.
//   bounded        forty callbacks before boot.
//   dom-event      a DOM event before boot, which is still dropped (#973).
import {readFileSync} from "node:fs";

const [bundlePath, scenario] = process.argv.slice(2);
if (!bundlePath || !scenario) {
    console.error("usage: node EarlyIslandCallbackFixture.mjs <rask.wasm.js path> <scenario>");
    process.exit(2);
}

interface StubElement {
    nodeType: number;
    tagName: string;
    _children: StubElement[];
    hasAttribute(name: string): boolean;
    getAttribute(name: string): string | null;
    setAttribute(name: string, value: string): void;
    removeAttribute(name: string): void;
    addEventListener(type: string, fn: (event: unknown) => void): void;
    appendChild(child: StubElement): StubElement;
    querySelectorAll?: (selector: string) => StubElement[];
    contains?: (node: unknown) => boolean;
}

function makeStubElement(tagName: string): StubElement {
    const attrs = new Map<string, string>();
    const el: StubElement = {
        nodeType: 1,
        tagName: tagName.toUpperCase(),
        _children: [],
        hasAttribute: (name: string) => attrs.has(name),
        getAttribute: (name: string) => attrs.get(name) ?? null,
        setAttribute: (name: string, value: string) => void attrs.set(name, String(value)),
        removeAttribute: (name: string) => void attrs.delete(name),
        addEventListener: () => { },
        appendChild: (child: StubElement) => {
            el._children.push(child);
            return child;
        },
    };
    return el;
}

/** An island host element whose props carry one callback per id. */
function island(name: string, ids: string[]): StubElement {
    const el = makeStubElement("rask-external");
    el.setAttribute("name", name);
    el.setAttribute("props", JSON.stringify(Object.fromEntries(ids.map((id, i) => [`on${i}`, {$h: id}]))));
    return el;
}

const head = makeStubElement("head");
head.querySelectorAll = () => [];

const body = makeStubElement("body");
body.contains = () => false;
body.setAttribute("data-rask-root", "");

let islands: StubElement[] = [];

const globals = globalThis as unknown as Record<string, unknown>;
globals.document = {
    head,
    body,
    documentElement: {tagName: "HTML", removeAttribute: () => { }},
    getElementById: () => null,
    createElement: (tag: string) => makeStubElement(tag),
    querySelector: (sel: string) => sel === "[data-rask-root]" ? body : null,
    querySelectorAll: (sel: string) => sel === "rask-external" ? islands : [],
    addEventListener: () => { },
};
globals.window = globalThis;
globals.addEventListener = () => { };
globals.performance = {getEntriesByName: () => [], now: () => 0};
globals.location = {pathname: "/", search: "", origin: "http://localhost"};
globals.history = {replaceState: () => { }, pushState: () => { }};
globals.cancelAnimationFrame = () => { };
globals.requestAnimationFrame = () => 0;
if (typeof globals.crypto === "undefined") {
    globals.crypto = {randomUUID: () => "stub-uuid"};
}

const realSetTimeout = globals.setTimeout as (fn: () => void, delay?: number) => number;
globals.setTimeout = (fn: () => void, delay?: number) => (delay ?? 0) >= 100 ? 0 : realSetTimeout(fn, delay);
globals.setInterval = () => 0;

const warnings: string[] = [];
console.warn = (...args: unknown[]) => void warnings.push(args.map(String).join(" "));

const bundleSource = readFileSync(bundlePath, "utf8");
const moduleUrl = "data:text/javascript;base64," + btoa(unescape(encodeURIComponent(bundleSource)));
const mod = await import(moduleUrl) as {
    setExports(exports: unknown): void;
    applyRender(payload: Uint8Array): void;
};

const host = (globalThis as unknown as { __raskHost: { send(payload: unknown): void } }).__raskHost;
const flush = (): Promise<void> => new Promise(resolve => realSetTimeout(() => resolve(), 0));

/** The handler ids .NET was asked to dispatch, in order. */
const dispatched: string[] = [];
const exports = {
    Rask: {
        Wasm: {
            JSInterop: {
                Dispatch: (bytes: Uint8Array) => {
                    // The callbacks held before boot are delivered in one task, so they cross as one batch.
                    const frame = JSON.parse(new TextDecoder().decode(bytes)) as { id: string; events?: { id: string }[] };
                    for (const event of frame.events ?? [frame]) dispatched.push(event.id);
                },
                EndInvokeJSResult: () => { },
            },
        },
    },
};

const callback = (id: string) => ({id, type: "external", args: [1]});
const firstFrame = () => mod.applyRender(new TextEncoder().encode(JSON.stringify({kind: "diff", ops: []})));

// The prerendered page: one Counter island, clickable before anything of .NET exists.
const ids = scenario === "bounded" ? Array.from({length: 40}, (_, i) => `h${i}`) : ["h3"];
islands = [island("Counter", ids)];

if (scenario === "dom-event") {
    host.send({id: "h3", type: "click"});
} else {
    for (const id of ids) host.send(callback(id));
}

const dispatchedBeforeBoot = dispatched.length;

if (scenario === "other-island") {
    // The app rendered a different page than the one that was prerendered: h3 is another island's now.
    islands = [island("Gauge", ["h3"])];
}

mod.setExports(exports);
firstFrame();
await flush();
await flush();

process.stdout.write(JSON.stringify({dispatchedBeforeBoot, dispatched, warnings}) + "\n");
