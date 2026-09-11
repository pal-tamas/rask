// Node-driven fixture for the external-component client runtime
// (src/Rask.External/wwwroot/rask-external.js).
//
// Drives the PRODUCTION runtime against a stub DOM and a fake adapter, so what is asserted is the
// real mount/update/unmount sequencing, the handler revival, and the props routing — not a C# port
// of any of it.
//
// The things that are easy to get wrong and impossible to see failing:
//   * a props change must UPDATE, never remount (React would lose all component state);
//   * a callback must keep its identity across updates (or every memo/useEffect keyed on it re-fires);
//   * calling a callback must reach the host's dispatch channel, with its args;
//   * `hydrate="none"` must never even request the chunk;
//   * an island's children must reach the adapter as nodes — never as a `$c` prop — with each child's
//     component loaded from its chunk once, however many renders repeat it.
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
            // Enough for the runtime's one query: a tag name.
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
let mountedChildren = null;
let handleSeq = 0;

const adapter = {
    mount(element, props, children) {
        log.push("mount");
        mountedProps = props;
        mountedChildren = children ?? null;
        return {id: ++handleSeq};
    },
    update(handle, props, children) {
        log.push("update");
        mountedProps = props;
        mountedChildren = children ?? null;
        return handle;
    },
    unmount() {
        log.push("unmount");
    },
};

// The runtime resolves a name to a module. Overridden so the fixture needs no bundler and no network. Every built entry
// exports its framework component beside the adapter, which is what a parent island loads a child by.
const requested = [];
globalThis.__raskExternal = {
    resolve: (name) => {
        requested.push(name);
        return Promise.resolve({default: adapter, component: `component:${name}`});
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

const tick = () => new Promise((r) => setTimeout(r, 0));

const island = makeEl("rask-external", {
    name: "Chart",
    module: "./Chart.tsx",
    props: JSON.stringify({heading: "Revenue", onPointClick: {$h: "c7:3"}}),
});
body.appendChild(island);

const stop = runtime.start(globalThis.document);
await tick();

const firstCallback = mountedProps && mountedProps.onPointClick;
const callbackIsFunction = typeof firstCallback === "function";

// A prop change. Must reach the adapter as an UPDATE.
island.setAttribute("props", JSON.stringify({heading: "Costs", onPointClick: {$h: "c7:3"}}));
await tick();

const secondCallback = mountedProps && mountedProps.onPointClick;
const headingAfterUpdate = mountedProps && mountedProps.heading;

// Calling it must reach the host channel with the arguments intact.
if (callbackIsFunction) firstCallback(42);

// Teardown.
runtime.__internals.unmount(island);
stop();

// A second island that must never fetch its chunk.
const inert = makeEl("rask-external", {name: "Inert", hydrate: "none", props: "{}"});
body.appendChild(inert);
runtime.__internals.hydrate(inert);
await tick();

const logBeforeChildren = [...log];
const requestedBeforeChildren = [...requested];

// ----- children -----
//
// The observer is stopped by now, so each attribute change is followed by the update the observer would have routed.

const parent = makeEl("rask-external", {
    name: "Card",
    props: JSON.stringify({
        heading: "Parent",
        $c: ["Revenue ", {n: "Badge", k: "b1", p: {label: "new", onPick: {$h: "c9:1"}}}],
    }),
});
body.appendChild(parent);
runtime.__internals.hydrate(parent);
await tick();
await tick();

const childrenOnMount = mountedChildren;
const propsOnMountHadChildrenKey = mountedProps !== null && Object.prototype.hasOwnProperty.call(mountedProps, "$c");
const badge = childrenOnMount && childrenOnMount[1];

// Same child, new label: its chunk has loaded, so the update needs no fetch and reaches the adapter in this same turn.
parent.setAttribute("props", JSON.stringify({
    heading: "Parent 2",
    $c: ["Revenue ", {n: "Badge", k: "b1", p: {label: "newer", onPick: {$h: "c9:1"}}}],
}));
runtime.__internals.update(parent);
const childrenSameTurn = mountedChildren;

// C# removed the children: the adapter is handed none.
parent.setAttribute("props", JSON.stringify({heading: "Parent 3"}));
runtime.__internals.update(parent);
const childrenAfterRemoval = mountedChildren;

runtime.__internals.unmount(parent);

process.stdout.write(JSON.stringify({
    log: logBeforeChildren,
    callbackIsFunction,
    // Same object across updates: the runtime's handler cache is keyed by id, and React compares
    // props by identity.
    callbackIdentityStable: firstCallback === secondCallback,
    headingAfterUpdate,
    dispatched,
    requested: requestedBeforeChildren,
    propsOnMountHadChildrenKey,
    childText: childrenOnMount ? childrenOnMount[0] : null,
    childName: badge ? badge.name : null,
    childKey: badge ? badge.key : null,
    childComponent: badge ? badge.component : null,
    childCallbackIsFunction: !!badge && typeof badge.props.onPick === "function",
    childLabelSameTurn: childrenSameTurn && childrenSameTurn[1] ? childrenSameTurn[1].props.label : null,
    childCallbackStable: !!badge && !!childrenSameTurn && childrenSameTurn[1].props.onPick === badge.props.onPick,
    childRequests: requested.filter((name) => name === "Badge").length,
    childrenAfterRemovalIsNull: childrenAfterRemoval === null,
}) + "\n");
