// Minimal DOM stub shared by the node-driven morph fixtures, which exercise the production
// Rask.Core/Resources/rask-morph.js outside a browser.
//
// Children are a true linked list (firstChild / nextSibling / previousSibling), and insertBefore
// throws exactly like a browser when the reference node isn't a child — that fidelity is the whole
// point: it is what surfaced the anchor-staleness bug KeyedHeadMorphFixture reproduces.
import {readFileSync} from "node:fs";

/** Installs the globals rask-morph.js reads, then loads it and returns its exports. */
export function loadMorph(morphPath) {
    globalThis.window = globalThis;
    globalThis.document = {
        activeElement: null,
        createElement: (name) => makeEl(String(name).toUpperCase())
    };

    const src = readFileSync(morphPath, "utf8");
    return new Function(src + "\n;return { morph };")();
}

export function makeEl(nodeName, attrs, text) {
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

/** An element of `nodeName` holding `children`, already parented. */
export function withKids(nodeName, children, attrs) {
    const parent = makeEl(nodeName, attrs);
    for (const c of children) parent.appendChild(c);
    return parent;
}
