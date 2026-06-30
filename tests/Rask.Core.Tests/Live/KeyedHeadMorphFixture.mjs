// Node-driven fixture for the shared client morph's keyed-children reconciliation
// (Rask.Core/Resources/rask-morph.js — the keyed branch under `if (keyed)`).
//
// Reproduces the WASM static-host hydration crash: the App renders a full <head>
// whose scoped-bundle <link> carries data-rask-key="rsk-css", which promotes the
// whole <head> to keyed reconciliation. The document it hydrates against is the
// SDK index.html <head> — <base> + an importmap <script> + <title>, none keyed.
// Those from-side nodes don't match the App's head by node name and get removed;
// pre-fix the keyed loop's `anchor` still pointed at a removed node, so the next
// insert threw "insertBefore ... reference node is not a child" and the runtime
// never finished its first morph (blank page). The fix advances the anchor past a
// node before removing it.
//
// The C# test (KeyedHeadMorphTests) runs this in a node subprocess and asserts the
// single JSON line on stdout. Pairs with the StandaloneWasm E2E (the host that
// exercises this exact hydration).
import {readFileSync} from "node:fs";

const morphPath = process.argv[2];
if (!morphPath) {
    console.error("usage: node KeyedHeadMorphFixture.mjs <rask-morph.js path>");
    process.exit(2);
}

globalThis.window = globalThis;
globalThis.document = {
    activeElement: null,
    createElement: (name) => makeEl(String(name).toUpperCase())
};

// ----- Minimal element stub with real insertBefore/removeChild semantics -----
// Children are a true linked list (firstChild / nextSibling), and insertBefore
// throws exactly like a browser when the reference node isn't a child — which is
// what surfaces the anchor-staleness bug.
function makeEl(nodeName, attrs, text) {
    const a = new Map(Object.entries(attrs || {}));
    const kids = [];

    function relink() {
        for (let i = 0; i < kids.length; i++) {
            kids[i].nextSibling = kids[i + 1] || null;
            kids[i].previousSibling = kids[i - 1] || null;
        }
    }

    const el = {
        nodeType: 1,
        nodeName,
        tagName: nodeName,
        parentNode: null,
        nextSibling: null,
        previousSibling: null,
        textContent: text || "",
        get firstChild() { return kids[0] || null; },
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => a.set(n, v),
        removeAttribute: (n) => a.delete(n),
        insertBefore(node, ref) {
            if (ref !== null && ref.parentNode !== el) {
                throw new Error("Failed to execute 'insertBefore' on 'Node': " +
                    "The node before which the new node is to be inserted is not a child of this node.");
            }
            if (node.parentNode) node.parentNode.removeChild(node);
            const idx = ref === null ? kids.length : kids.indexOf(ref);
            kids.splice(idx, 0, node);
            node.parentNode = el;
            relink();
            return node;
        },
        appendChild(node) { return el.insertBefore(node, null); },
        removeChild(node) {
            if (node.parentNode !== el) throw new Error("removeChild: node is not a child");
            kids.splice(kids.indexOf(node), 1);
            node.parentNode = null;
            relink();
            return node;
        },
        replaceChild(newNode, oldNode) {
            el.insertBefore(newNode, oldNode);
            el.removeChild(oldNode);
            return oldNode;
        },
        _kids: kids
    };
    return el;
}

function head(children) {
    const h = makeEl("HEAD");
    for (const c of children) h.appendChild(c);
    return h;
}

const src = readFileSync(morphPath, "utf8");
const {morph} = new Function(src + "\n;return { morph };")();

// from = the SDK index.html <head> a WASM static host serves (no data-rask-key).
const fromHead = head([
    makeEl("BASE", {href: "/"}),
    makeEl("SCRIPT", {type: "importmap"}, "{}"),
    makeEl("TITLE", {}, "Old")
]);

// to = the App's rendered <head>: a <title> plus the keyed scoped-bundle <link>.
const toHead = head([
    makeEl("TITLE", {}, "Rask"),
    makeEl("LINK", {rel: "stylesheet", href: "/_rask/a/abc123def456.css", "data-rask-key": "rsk-css"})
]);

let threw = false;
let error = "";
try {
    morph(fromHead, toHead);
} catch (e) {
    threw = true;
    error = String((e && e.message) || e);
}

process.stdout.write(JSON.stringify({
    threw,
    error,
    children: fromHead._kids.map((c) => c.nodeName)
}) + "\n");
