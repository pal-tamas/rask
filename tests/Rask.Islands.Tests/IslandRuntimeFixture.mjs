// Node-driven fixture for the island client runtime (src/Rask.Islands/wwwroot/rask-islands.js).
//
// Drives the PRODUCTION runtime against a stub DOM and a fake adapter, so what is asserted is the
// real mount/update/unmount sequencing, the handler revival, and the props routing — not a C# port
// of any of it.
//
// The four things that are easy to get wrong and impossible to see failing:
//   * a props change must UPDATE, never remount (React would lose all component state);
//   * a callback must keep its identity across updates (or every memo/useEffect keyed on it re-fires);
//   * calling a callback must reach the host's dispatch channel, with its args;
//   * `hydrate="none"` must never even request the chunk.
//
// The C# test (IslandRuntimeTests) runs this and asserts the JSON on stdout.
import { readFileSync, writeFileSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const runtimePath = process.argv[2];
if (!runtimePath) {
    console.error("usage: node IslandRuntimeFixture.mjs <rask-islands.js path>");
    process.exit(2);
}

// ----- stub DOM, only what the runtime touches -----

const observers = [];

function makeEl(tagName, attrs) {
    const a = new Map(Object.entries(attrs || {}));
    const kids = [];
    const el = {
        nodeType: 1,
        tagName: tagName.toUpperCase(),
        isConnected: true,
        parentNode: null,
        get firstChild() { return kids[0] || null; },
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => {
            a.set(n, v);
            // Stands in for the browser's MutationObserver: the runtime subscribes to `props` and
            // routes a change to the adapter, which is the whole point of the boundary.
            for (const cb of observers) cb([{type: "attributes", target: el, attributeName: n}]);
        },
        removeAttribute: (n) => a.delete(n),
        appendChild: (node) => { kids.push(node); node.parentNode = el; return node; },
        remove: () => { el.isConnected = false; },
        querySelectorAll: (sel) => kids.filter((k) => k.tagName === sel.toUpperCase()),
        _kids: kids,
    };
    return el;
}

const body = makeEl("body");
globalThis.document = {
    body,
    createElement: (name) => makeEl(name),
};
globalThis.MutationObserver = class {
    constructor(cb) { this._cb = cb; }
    observe() { observers.push(this._cb); }
    disconnect() { const i = observers.indexOf(this._cb); if (i >= 0) observers.splice(i, 1); }
};
globalThis.IntersectionObserver = undefined;

// ----- the fake adapter, and the record of what the runtime did to it -----

const log = [];
let mountedProps = null;
let handleSeq = 0;

const adapter = {
    mount(element, props) {
        log.push("mount");
        mountedProps = props;
        return {id: ++handleSeq};
    },
    update(handle, props) {
        log.push("update");
        mountedProps = props;
        return handle;
    },
    unmount() {
        log.push("unmount");
    },
};

// The runtime resolves a name to a module. Overridden so the fixture needs no bundler and no network.
const requested = [];
globalThis.__raskIslands = {
    resolve: (name) => {
        requested.push(name);
        return Promise.resolve({default: adapter});
    },
};

// The host's dispatch channel, which is what an island callback must reach.
const dispatched = [];
globalThis.__raskHost = {send: (payload) => dispatched.push(payload)};

// The runtime is an ES module and auto-starts on import; hold it back so the fixture controls timing.
globalThis.__raskIslandsManual = true;

// Loaded from a temp copy so the import specifier is a file URL on every platform.
const dir = mkdtempSync(join(tmpdir(), "rask-islands-"));
const copy = join(dir, "rask-islands.mjs");
writeFileSync(copy, readFileSync(runtimePath, "utf8"));
const runtime = await import(pathToFileURL(copy).href);

// ----- the run -----

const island = makeEl("rask-island", {
    name: "Chart",
    module: "./Chart.tsx",
    props: JSON.stringify({heading: "Revenue", onPointClick: {$h: "c7:3"}}),
});
body.appendChild(island);

const stop = runtime.start(globalThis.document);
await new Promise((r) => setTimeout(r, 0));

const firstCallback = mountedProps && mountedProps.onPointClick;
const callbackIsFunction = typeof firstCallback === "function";

// A prop change. Must reach the adapter as an UPDATE.
island.setAttribute("props", JSON.stringify({heading: "Costs", onPointClick: {$h: "c7:3"}}));
await new Promise((r) => setTimeout(r, 0));

const secondCallback = mountedProps && mountedProps.onPointClick;

// Calling it must reach the host channel with the arguments intact.
if (callbackIsFunction) firstCallback(42);

// Teardown.
runtime.__internals.unmount(island);
stop();

// A second island that must never fetch its chunk.
const inert = makeEl("rask-island", {name: "Inert", hydrate: "none", props: "{}"});
body.appendChild(inert);
runtime.__internals.hydrate(inert);
await new Promise((r) => setTimeout(r, 0));

process.stdout.write(JSON.stringify({
    log,
    callbackIsFunction,
    // Same object across updates: the runtime's handler cache is keyed by id, and React compares
    // props by identity.
    callbackIdentityStable: firstCallback === secondCallback,
    headingAfterUpdate: mountedProps && mountedProps.heading,
    dispatched,
    requested,
}) + "\n");
