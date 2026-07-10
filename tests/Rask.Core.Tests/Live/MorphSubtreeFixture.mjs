// Node-driven fixture for the shared diff interpreter's MorphSubtree op
// (Rask.Core/Resources/rask-dom.js applyDiff `case 8`, which delegates to
// Rask.Core/Resources/rask-morph.js `morph`).
//
// MorphSubtree is the Raw-tainted fallback shrunk from a full-document morph to ONE
// parent's children: instead of re-morphing document.documentElement (the flaky,
// expensive path on every guide/CodeSample page), the server ships a trusted op that
// carries the Raw-owning parent's new inner HTML, and the client morphs just that
// subtree. This fixture drives the production applyDiff+morph in a Node subprocess with
// a stub DOM (a minimal HTML parser backs innerHTML) and asserts three things:
//   1. the op is recognised (no location.reload fallback),
//   2. the tainted subtree converges — a Raw-expanded multi-node run reconciles and the
//      changed sibling's text updates, including a node-count change,
//   3. a focused node OUTSIDE the morphed parent keeps focus (morph is scoped, not global).
//
// The C# test (MorphSubtreeTests) runs this and asserts the single JSON line on stdout.
// Real-browser coverage of the same op lives in the Playwright E2E guide journeys.
import {readFileSync} from "node:fs";

const morphPath = process.argv[2];
const domPath = process.argv[3];
if (!morphPath || !domPath) {
    console.error("usage: node MorphSubtreeFixture.mjs <rask-morph.js> <rask-dom.js>");
    process.exit(2);
}

let reloaded = false;
globalThis.window = globalThis;
globalThis.location = {reload: () => { reloaded = true; }};
// Deliberately NO MutationObserver and NO document.addEventListener: rask-dom.js's
// install* IIFEs (focus trap / popover / reload) then early-return on load, so the
// fixture exercises only applyDiff + morph.

// ----- Minimal DOM stub with a tiny HTML parser behind innerHTML -----------------
function makeText(value) {
    return {
        nodeType: 3, nodeName: "#text", nodeValue: value,
        parentNode: null, nextSibling: null, previousSibling: null
    };
}

function makeEl(nodeName, attrs) {
    const a = new Map(Object.entries(attrs || {}));
    const kids = [];

    function relink() {
        for (let i = 0; i < kids.length; i++) {
            kids[i].nextSibling = kids[i + 1] || null;
            kids[i].previousSibling = kids[i - 1] || null;
        }
    }

    const el = {
        nodeType: 1, nodeName, tagName: nodeName, parentNode: null,
        nextSibling: null, previousSibling: null,
        get firstChild() { return kids[0] || null; },
        get childNodes() { return kids; },
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => a.set(n, String(v)),
        removeAttribute: (n) => a.delete(n),
        cloneNode() { return makeEl(nodeName, Object.fromEntries(a)); }, // shallow: attrs, no kids
        set innerHTML(html) {
            while (kids.length) el.removeChild(kids[0]);
            for (const n of parseHtml(html)) el.appendChild(n);
        },
        insertBefore(node, ref) {
            if (node.parentNode) node.parentNode.removeChild(node);
            const idx = ref === null ? kids.length : kids.indexOf(ref);
            kids.splice(idx < 0 ? kids.length : idx, 0, node);
            node.parentNode = el;
            relink();
            return node;
        },
        appendChild(node) { return el.insertBefore(node, null); },
        removeChild(node) {
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

// Tiny well-formed-HTML parser — enough for the flat tag/text fragments this fixture
// feeds (no attributes needed). Returns an array of top-level nodes.
function parseHtml(str) {
    const roots = [];
    const stack = [];
    const push = (node) => { if (stack.length) stack[stack.length - 1].appendChild(node); else roots.push(node); };
    let i = 0;
    while (i < str.length) {
        if (str[i] === "<") {
            const gt = str.indexOf(">", i);
            let tag = str.slice(i + 1, gt).trim();
            i = gt + 1;
            if (tag.startsWith("/")) { stack.pop(); continue; }
            const selfClose = tag.endsWith("/");
            if (selfClose) tag = tag.slice(0, -1).trim();
            const el = makeEl(tag.split(/\s+/)[0].toUpperCase());
            push(el);
            if (!selfClose) stack.push(el);
        } else {
            const lt = str.indexOf("<", i);
            const end = lt < 0 ? str.length : lt;
            const text = str.slice(i, end);
            i = end;
            if (text.length) push(makeText(text));
        }
    }
    return roots;
}

globalThis.document = {
    activeElement: null,
    createElement: (name) => makeEl(String(name).toUpperCase())
};
// resolvePath starts at `document` and walks childNodes — give the document a child list.
const docKids = [];
Object.defineProperty(document, "childNodes", {get: () => docKids});

// ----- Load the production interpreter (morph + applyDiff, spliced order) --------
const src = readFileSync(morphPath, "utf8") + "\n" + readFileSync(domPath, "utf8");
const {applyDiff} = new Function(src + "\n;return { applyDiff };")();

// ----- Build the "before" tree ---------------------------------------------------
// document
//   [0] <div id=container>  ← the Raw-owning parent (Raw expanded to <a><b>, plus <span>x)
//         <a></a><b></b><span>x</span>
//   [1] <input id=focused>  ← a focused sibling OUTSIDE the morphed parent
const container = makeEl("DIV", {id: "container"});
container.innerHTML = "<a></a><b></b><span>x</span>";
const focused = makeEl("INPUT", {id: "focused"});
docKids.push(container, focused);
container.parentNode = document;
focused.parentNode = document;
document.activeElement = focused;

// ----- Apply the MorphSubtree op -------------------------------------------------
// [8, [0], innerHtml] — the container's NEW inner HTML: the Raw run changed shape (the
// <b> is gone) and the sibling <span> text flipped x → y. A full-document morph is what
// this used to be; now it's scoped to the container.
let threw = false;
let error = "";
try {
    applyDiff([[8, [0], "<a></a><span>y</span>"]], null);
} catch (e) {
    threw = true;
    error = String((e && e.stack) || (e && e.message) || e);
}

process.stdout.write(JSON.stringify({
    threw,
    error,
    reloaded,
    // container children after the scoped morph: the <b> removed, <span> kept.
    children: container._kids.map((c) => c.nodeName),
    spanText: (() => {
        const span = container._kids.find((c) => c.nodeName === "SPAN");
        return span && span._kids[0] ? span._kids[0].nodeValue : null;
    })(),
    // the focused input outside the morphed parent must keep focus (identity + activeElement).
    focusKept: document.activeElement === focused && focused.parentNode === document
}) + "\n");
