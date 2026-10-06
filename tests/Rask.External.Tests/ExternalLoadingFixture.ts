// Node-driven fixture for the `Loading` placeholder in the external-component client runtime
// (src/Rask.External/wwwroot/rask-external.js).
//
// C# renders the placeholder inside the host element for the first paint. The runtime has to remove it
// exactly once, right before the first mount — not while a deferred island is still waiting, and never
// again afterwards, or it would take the nodes the adapter put there with it.
//
// The C# test (ExternalRuntimeTests) runs this and asserts the JSON on stdout.

function makeEl(tagName, attrs) {
    const a = new Map(Object.entries(attrs || {}));
    const kids = [];
    const el = {
        nodeType: 1,
        tagName: tagName.toUpperCase(),
        isConnected: true,
        get firstChild() { return kids[0] || null; },
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => { a.set(n, v); },
        appendChild: (node) => { kids.push(node); return node; },
        replaceChildren: (...nodes) => { kids.splice(0, kids.length, ...nodes); },
        querySelectorAll: () => [],
        names: () => kids.map((kid) => kid.getAttribute("class")),
    };
    return el;
}

/** A host element as the server sends it: the island's attributes, and the placeholder inside. */
function island(name, hydrate) {
    const el = makeEl("rask-external", {name, props: "{}", ...(hydrate ? {hydrate} : {})});
    el.appendChild(makeEl("div", {class: "skeleton"}));
    return el;
}

globalThis.document = {body: makeEl("body"), createElement: (name) => makeEl(name)};
globalThis.MutationObserver = class {
    observe() {}
    disconnect() {}
};
globalThis.IntersectionObserver = undefined;

// `idle` waits on this, so the fixture decides when the deferred island mounts.
let runIdle = null;
globalThis.requestIdleCallback = (fn) => { runIdle = fn; return 1; };
globalThis.cancelIdleCallback = () => { runIdle = null; };

// What the placeholder looked like at the moment the adapter was handed the element.
const seenByMount = {};
const adapter = {
    mount(element) {
        seenByMount[element.getAttribute("name")] = element.names();
        element.appendChild(makeEl("div", {class: "mounted"}));
        return element;
    },
    update(element) {
        return element;
    },
    unmount() {},
};

globalThis.__raskExternal = {resolve: () => Promise.resolve({default: adapter})};
globalThis.__raskExternalManual = true;

const runtime = await import("../../src/Rask.External/wwwroot/rask-external.js");
const tick = () => new Promise((r) => setTimeout(r, 0));

const eager = island("Eager");
const deferred = island("Deferred", "idle");
const inert = island("Inert", "none");
for (const el of [eager, deferred, inert]) runtime.__internals.hydrate(el);
await tick();

const eagerAfterMount = eager.names();
const deferredWhileWaiting = deferred.names();

// A prop change after the mount: the nodes in the host are the adapter's now.
eager.setAttribute("props", JSON.stringify({heading: "again"}));
runtime.__internals.update(eager);
const eagerAfterUpdate = eager.names();

runIdle?.();
await tick();

process.stdout.write(JSON.stringify({
    seenByEagerMount: seenByMount.Eager ?? null,
    eagerAfterMount,
    eagerAfterUpdate,
    deferredWhileWaiting,
    deferredAfterMount: deferred.names(),
    inertNeverMounted: inert.names(),
}) + "\n");
