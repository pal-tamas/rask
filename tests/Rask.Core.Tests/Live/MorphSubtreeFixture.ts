// Node-driven fixture for the shared diff interpreter's MorphSubtree op
// (Rask.Core/Resources/rask-dom.ts applyDiff `case 8`, which delegates to
// Rask.Core/Resources/rask-morph.ts `morph`).
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
//
// The functions under test arrive by IMPORT. They used to be read off disk and evaluated with
// `new Function(src + "return { … }")`, because the shared modules were bare declarations meant to be
// pasted into a host's scope — there was no other way to reach them, nothing checked that the names
// in that string still existed, and it stops working the moment a module has real `export`s.

import {applyDiff} from "../../../src/Rask.Core/Resources/rask-dom.js";
import {asStubParent, installStubGlobals, type StubNode, type StubParent} from "./stub-dom.js";


let reloaded = false;
(globalThis as unknown as { location: unknown }).location = {reload: () => { reloaded = true; }};
// Deliberately NO MutationObserver and NO document.addEventListener: rask-dom.ts's
// install* IIFEs (focus trap / popover / reload) then early-return on load, so the
// fixture exercises only applyDiff + morph.

// ----- Minimal DOM stub with a tiny HTML parser behind innerHTML -----------------
function makeText(value: string): StubNode {
    // One cast, at the boundary where the fiction is created. A text node is not a parent, so this
    // is the narrower StubNode rather than the StubParent the element factory below returns.
    return {
        nodeType: 3,
        nodeName: "#text",
        // Both, because the framework reads both on the same node: the morph compares `nodeValue`
        // for nodeType 3, and the diff codec assigns `textContent`.
        nodeValue: value,
        textContent: value,
        parentNode: null,
        nextSibling: null,
        previousSibling: null,
    } as unknown as StubNode;
}

function makeEl(nodeName: string, attrs?: Record<string, string>): StubParent {
    const a = new Map(Object.entries(attrs || {}));
    const kids: StubNode[] = [];

    function relink() {
        for (let i = 0; i < kids.length; i++) {
            kids[i].nextSibling = kids[i + 1] || null;
            kids[i].previousSibling = kids[i - 1] || null;
        }
    }

    const el: StubParent = {
        nodeType: 1, nodeValue: null, nodeName, tagName: nodeName, parentNode: null,
        nextSibling: null, previousSibling: null, textContent: "",
        get firstChild() { return kids[0] || null; },
        get childNodes() { return kids; },
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, String(v)),
        removeAttribute: (n: string) => a.delete(n),
        cloneNode() { return makeEl(nodeName, Object.fromEntries(a)); }, // shallow: attrs, no kids
        set innerHTML(html: string) {
            while (kids.length) el.removeChild(kids[0]);
            for (const n of parseHtml(html)) el.appendChild(n);
        },
        insertBefore(node: StubNode, ref: StubNode | null) {
            if (node.parentNode) node.parentNode.removeChild(node);
            const idx = ref === null ? kids.length : kids.indexOf(ref);
            kids.splice(idx < 0 ? kids.length : idx, 0, node);
            node.parentNode = el;
            relink();
            return node;
        },
        appendChild(node: StubNode) { return el.insertBefore(node, null); },
        removeChild(node: StubNode) {
            kids.splice(kids.indexOf(node), 1);
            node.parentNode = null;
            relink();
            return node;
        },
        replaceChild(newNode: StubNode, oldNode: StubNode) {
            el.insertBefore(newNode, oldNode);
            el.removeChild(oldNode);
            return oldNode;
        },
        _kids: kids
    };
    // One cast, at the boundary where the fiction is created: a partial fake asserted to be
    // the element it stands in for. Everything below reads as ordinary code, checked against
    // the real interface.
    return el as unknown as StubParent;
}

// Tiny well-formed-HTML parser — enough for the flat tag/text fragments this fixture
// feeds (no attributes needed). Returns an array of top-level nodes.
function parseHtml(str: string): StubNode[] {
    const roots: StubNode[] = [];
    const stack: StubParent[] = [];
    const push = (node: StubNode) => {
        if (stack.length) stack[stack.length - 1].appendChild(node);
        else roots.push(node);
    };
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

// resolvePath starts at `document` and walks childNodes, so the stub needs one.
const docKids: StubNode[] = [];
const doc = {
    nodeType: 9,
    activeElement: null as StubNode | null,
    childNodes: docKids,
    createElement: (name?: string) => makeEl(String(name).toUpperCase()),
};

installStubGlobals(doc);


// ----- Build the "before" tree ---------------------------------------------------
// document
//   [0] <div id=container>  ← the Raw-owning parent (Raw expanded to <a><b>, plus <span>x)
//         <a></a><b></b><span>x</span>
//   [1] <input id=focused>  ← a focused sibling OUTSIDE the morphed parent
const container = makeEl("DIV", {id: "container"});
container.innerHTML = "<a></a><b></b><span>x</span>";
const focused = makeEl("INPUT", {id: "focused"});
docKids.push(container, focused);
// The document stands in as the parent here; only its identity matters to the assertions.
const docAsParent = doc as unknown as StubParent;
container.parentNode = docAsParent;
focused.parentNode = docAsParent;
doc.activeElement = focused;

// ----- Apply the MorphSubtree op -------------------------------------------------
// [8, [0], innerHtml] — the container's NEW inner HTML: the Raw run changed shape (the
// <b> is gone) and the sibling <span> text flipped x → y. A full-document morph is what
// this used to be; now it's scoped to the container.
let threw = false;
let error = "";
try {
    applyDiff([[8, [0], "<a></a><span>y</span>"]]);
} catch (e) {
    threw = true;
    error = e instanceof Error ? (e.stack ?? e.message) : String(e);
}

process.stdout.write(JSON.stringify({
    threw,
    error,
    reloaded,
    // container children after the scoped morph: the <b> removed, <span> kept.
    children: container._kids.map((c) => c.nodeName),
    spanText: (() => {
        const span = container._kids.find((c) => c.nodeName === "SPAN");
        const text = span ? asStubParent(span)._kids[0] : null;
        return text ? text.nodeValue : null;
    })(),
    // the focused input outside the morphed parent must keep focus (identity + activeElement).
    focusKept: doc.activeElement === focused && focused.parentNode === docAsParent
}) + "\n");
