// Node-driven fixture for the external-component client runtime
// (src/Rask.External/wwwroot/rask-external.js).
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
// The C# test (ExternalRuntimeTests) runs this and asserts the JSON on stdout.
// The runtime is an ES module and auto-starts on import, so the flag has to be set before the import
// is evaluated. A static import is hoisted above every statement in this file, which is exactly why
// this one is dynamic and sits below the flag.
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
        remove: () => {
            // Detach from the parent as a real remove() does, not just flip a flag — otherwise a test
            // asserting the node is gone passes on a runtime that never removed it.
            el.isConnected = false;
            const siblings = el.parentNode && el.parentNode._kids;
            if (siblings) {
                const i = siblings.indexOf(el);
                if (i >= 0) siblings.splice(i, 1);
            }
            el.parentNode = null;
        },
        querySelectorAll: (sel) => {
            // Enough for the runtime's one query: the host tag name.
            const out = [];
            const walk = (n) => {
                for (const k of n._kids || []) {
                    if (k.tagName === sel.toUpperCase()) out.push(k);
                    walk(k);
                }
            };
            walk(el);
            return out;
        },
        closest: (sel) => {
            let n = el;
            while (n) {
                if (n.tagName === sel.toUpperCase()) return n;
                n = n.parentNode;
            }
            return null;
        },
        content: null,
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
globalThis.__raskExternal = {
    resolve: (name) => {
        requested.push(name);
        return Promise.resolve({default: adapter});
    },
};

// The host's dispatch channel, which is what an island callback must reach.
const dispatched = [];
globalThis.__raskHost = {send: (payload) => dispatched.push(payload)};

// Hold the runtime back so the fixture controls mount timing.
globalThis.__raskExternalManual = true;

// Imported, not read off disk: esbuild resolves the real module at BUILD time, so a rename or a
// removed export fails the build rather than this fixture at runtime.
const runtime = await import("../../src/Rask.External/wwwroot/rask-external.js");

// ----- the run -----

const island = makeEl("rask-external", {
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
const inert = makeEl("rask-external", {name: "Inert", hydrate: "none", props: "{}"});
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
