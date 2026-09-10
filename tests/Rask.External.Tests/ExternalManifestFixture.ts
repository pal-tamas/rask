// Node-driven fixture for the client runtime's MANIFEST RESOLUTION
// (src/Rask.External/wwwroot/rask-external.js — defaultResolve).
//
// This one deliberately does NOT override `__raskExternal.resolve`, unlike ExternalRuntimeFixture:
// the resolver is the code under test here, so replacing it would leave the whole path untested.
// `fetch` is stubbed instead, which is the seam the resolver actually reaches for.
//
// The bug (#939): an island had to be bundled by the APP that serves the page, because the manifest
// URL was a hard-coded, app-rooted constant. A class library's static web assets are served under
// `_content/<PackageId>/`, so a library-built bundle lands where nothing looked for it — and a
// single cached manifest promise meant the first manifest to load was the only one that could
// exist, so a page showing islands from the app AND from a library could never resolve both.
//
// So the claims are: a host element with no `manifest` attribute uses the app's own; one that names
// a manifest uses that; both work on the same page; and each URL is fetched exactly once however
// many islands resolve through it.

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
            for (const cb of observers) cb([{type: "attributes", target: el, attributeName: n}]);
        },
        removeAttribute: (n) => a.delete(n),
        appendChild: (node) => { kids.push(node); node.parentNode = el; return node; },
        remove: () => {
            el.isConnected = false;
            const siblings = el.parentNode && el.parentNode._kids;
            if (siblings) {
                const i = siblings.indexOf(el);
                if (i >= 0) siblings.splice(i, 1);
            }
            el.parentNode = null;
        },
        querySelectorAll: (sel) => {
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

// Each island's chunk is a REAL module, imported for real: a data: URL Node can evaluate. Nothing
// about the resolve path is stubbed except `fetch`, so what is exercised is the production code that
// reads the manifest attribute, picks a manifest, fetches it and looks the name up in it.
globalThis.__raskMounted = [];

function chunk(island) {
    return "data:text/javascript," + encodeURIComponent(
        "export default {" +
        `mount() { globalThis.__raskMounted.push(${JSON.stringify(island)}); return {}; },` +
        "update(handle) { return handle; }," +
        "unmount() {} }");
}

// Two manifests, each holding an island the other does not. Serving the wrong one is therefore not a
// near miss -- the name is simply absent and the resolve reports it.
const APP_MANIFEST = "/_rask/external/manifest.json";
const LIB_MANIFEST = "/_content/Acme.Ui/_rask/external/manifest.json";

const tables = {
    [APP_MANIFEST]: {Chart: chunk("Chart")},
    [LIB_MANIFEST]: {Gauge: chunk("Gauge")},
};

const fetched = [];
globalThis.fetch = (url) => {
    fetched.push(url);
    const table = tables[url];
    if (!table) return Promise.resolve({ok: false, status: 404});
    return Promise.resolve({ok: true, status: 200, json: () => Promise.resolve(table)});
};

globalThis.__raskExternalManual = true;

const runtime = await import("../../src/Rask.External/wwwroot/rask-external.js");

// An app-owned island (no manifest attribute) and a library-owned one, on the same page.
body.appendChild(makeEl("rask-external", {name: "Chart", props: "{}"}));
body.appendChild(makeEl("rask-external", {name: "Gauge", props: "{}", manifest: LIB_MANIFEST}));

// A second island from the library's manifest, to prove the fetch is cached per URL rather than per
// element.
body.appendChild(makeEl("rask-external", {name: "Gauge", props: "{}", manifest: LIB_MANIFEST}));

const errors = [];
const consoleError = console.error;
console.error = (...args) => errors.push(args.map(String).join(" "));

const stop = runtime.start(globalThis.document);
await new Promise((r) => setTimeout(r, 0));
await new Promise((r) => setTimeout(r, 0));
await new Promise((r) => setTimeout(r, 0));

console.error = consoleError;
stop && stop();

process.stdout.write(JSON.stringify({
    fetched,
    appFetches: fetched.filter((u) => u === APP_MANIFEST).length,
    libFetches: fetched.filter((u) => u === LIB_MANIFEST).length,
    mountedNames: globalThis.__raskMounted,
    errors,
}) + "\n");
