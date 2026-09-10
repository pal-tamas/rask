// Node-driven fixture for the keyed reconciliation's ANCHOR
// (Rask.Core/Resources/rask-morph.ts — the `if (keyed)` branch).
//
// A prerendered <head> is [shell nodes][document nodes], while the runtime's full-frame payload carries
// the document's alone. So the from-side opens with nodes the incoming tree does not claim, and the
// anchor used to start on the first of them. Everything the payload DID claim then looked out of place
// and was relocated in front of that doomed node — 22 moveBefore calls on the landing page, for a head
// whose surviving nodes were already in the right order.
//
// That is not free. Moving a <link rel=stylesheet> re-resolves its sheet: measured on the published
// bundle, three of four stylesheets left document.styleSheets for ~37ms while their elements stayed
// connected, unremoved and un-mutated. One completely unstyled frame on every load (#1049).
//
// The anchor now skips nodes the incoming tree does not claim, walking the LIVE sibling chain rather
// than the snapshot the loop started with — the DOM is mutated as it goes, so a snapshot stops
// describing it after the first insert.

import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, installStubGlobals, type StubNode, type StubParent} from "./stub-dom.js";

installStubGlobals({
    activeElement: null,
    createElement: (name) => makeEl(String(name).toUpperCase())
});

let moves = 0;

function makeEl(nodeName: string, attrs?: Record<string, string>, text?: string): StubParent {
    const a = new Map(Object.entries(attrs || {}));
    const kids: StubNode[] = [];

    function relink() {
        for (let i = 0; i < kids.length; i++) {
            kids[i].nextSibling = kids[i + 1] || null;
            kids[i].previousSibling = kids[i - 1] || null;
        }
    }

    const el: StubParent = {
        nodeType: 1,
        nodeValue: null,
        nodeName,
        tagName: nodeName,
        parentNode: null,
        previousSibling: null,
        nextSibling: null,
        textContent: text || "",
        innerHTML: "",
        get firstChild() { return kids[0] || null; },
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, v),
        removeAttribute: (n: string) => a.delete(n),
        insertBefore(node: StubNode, ref: StubNode | null) {
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
        appendChild(node: StubNode) { return el.insertBefore(node, null); },
        removeChild(node: StubNode) {
            if (node.parentNode !== el) throw new Error("removeChild: node is not a child");
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
        // The runtime prefers the atomic move API. Counting it is the whole measurement: a moved
        // stylesheet link is what loses its sheet, and the browser reports the move as a
        // removal+addition either way, so a mutation log cannot tell the two apart.
        moveBefore(node: StubNode, ref: StubNode | null) {
            moves++;
            return el.insertBefore(node, ref);
        },
        _kids: kids
    } as StubParent;
    return el;
}

function head(children: StubNode[]): StubParent {
    const h = makeEl("HEAD");
    for (const c of children) h.appendChild(c);
    return h;
}

function keyed(tag: string, key: string, extra?: Record<string, string>): StubParent {
    return makeEl(tag, {...(extra || {}), "data-rask-key": key});
}

// from = what the prerendered page serves: the SDK shell's own head, then the document's.
const shellBase = makeEl("BASE", {href: "/"});
const shellScript = makeEl("SCRIPT", {type: "importmap"}, "{}");
const shellStyle = makeEl("STYLE", {}, ".rask-boot{}");
const docTitle = keyed("TITLE", "tag:title");
const docCss = keyed("LINK", "h-css", {rel: "stylesheet", href: "/css/app.css"});
const docMeta = keyed("META", "tag:meta:description", {name: "description", content: "x"});

const fromHead = head([shellBase, shellScript, shellStyle, docTitle, docCss, docMeta]);

// to = the runtime's payload: the document's head only, in the same relative order.
const toHead = head([
    keyed("TITLE", "tag:title"),
    keyed("LINK", "h-css", {rel: "stylesheet", href: "/css/app.css"}),
    keyed("META", "tag:meta:description", {name: "description", content: "x"})
]);

let threw = false;
let error = "";
try {
    morph(asDom(fromHead), asDom(toHead));
} catch (e) {
    threw = true;
    error = String((e && (e instanceof Error ? e.message : String(e))) || e);
}

process.stdout.write(JSON.stringify({
    threw,
    error,
    moves,
    // The surviving nodes must be the SAME OBJECTS: a stylesheet link that is replaced, or moved,
    // loses its applied sheet.
    sameTitle: fromHead._kids.includes(docTitle),
    sameCss: fromHead._kids.includes(docCss),
    sameMeta: fromHead._kids.includes(docMeta),
    children: fromHead._kids.map((c) => c.nodeName)
}) + "\n");
