// Node-driven fixture for the shared client morph's POSITIONAL child pairing
// (Rask.Core/Resources/rask-morph.ts — the walk under `const max = Math.max(fc.length, tc.length)`).
//
// Reproduces the WASM prerender hydration flash: the published page paints one completely unstyled
// frame — UA serif, transparent ground, 13x the height — as the prerendered document hands over to
// the runtime.
//
// The trigger is a single whitespace text node. The SDK pretty-prints index.html, so the served page
// contains `</head>\n<body …>`, and per the HTML parser's "after head" insertion mode that newline is
// inserted into the <html> element. The live document's <html> therefore has children
// [HEAD, #text, BODY], while the runtime's full-frame payload — HtmlSerializer emits no newlines —
// parses to [HEAD, BODY].
//
// Neither child is keyed, so the positional walk pairs them by index:
//   HEAD  <-> HEAD   morphed in place        (fine)
//   #text <-> BODY   node names differ  -->  _raskReplaceChild inserts a BRAND-NEW <body>
//   BODY  <-> —      no counterpart     -->  removed
//
// A freshly created <body> has no resolved style yet, which is the unstyled frame. The fix drops
// formatting whitespace from BOTH sides when pairing the children of <html>/<head> — the containers
// where such text is never rendered — so HEAD pairs with HEAD and BODY with BODY.
//
// The C# test (PrerenderWhitespaceMorphTests) runs this in a node subprocess and asserts the single
// JSON line on stdout.

import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, installStubGlobals, type StubNode, type StubParent} from "./stub-dom.js";

installStubGlobals({
    activeElement: null,
    createElement: (name) => makeEl(String(name).toUpperCase())
});

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
        _kids: kids
    };
    return el;
}

/** A whitespace-only text node — exactly what the parser puts in <html> for `</head>\n<body>`. */
function makeText(value: string): StubNode {
    return {
        nodeType: 3,
        nodeName: "#text",
        textContent: value,
        nodeValue: value,
        parentNode: null,
        previousSibling: null,
        nextSibling: null,
        firstChild: null
    };
}

function tree(nodeName: string, children: StubNode[]): StubParent {
    const el = makeEl(nodeName);
    for (const c of children) el.appendChild(c);
    return el;
}

function div(id: string, text: string): StubParent {
    const el = makeEl("DIV", {id});
    el.appendChild(makeText(text));
    return el;
}

// from = the SERVED prerendered document: <html> holding [HEAD, #text("\n"), BODY].
const liveDiv = div("app", "hello");
const liveBody = tree("BODY", [liveDiv]);
const fromHtml = tree("HTML", [
    tree("HEAD", [makeEl("TITLE", {}, "Rask")]),
    makeText("\n"),
    liveBody
]);

// to = the runtime's full-frame payload: <html> holding [HEAD, BODY], no formatting text at all.
const toHtml = tree("HTML", [
    tree("HEAD", [makeEl("TITLE", {}, "Rask")]),
    tree("BODY", [div("app", "hello there")])
]);

let threw = false;
let error = "";
try {
    morph(asDom(fromHtml), asDom(toHtml));
} catch (e) {
    threw = true;
    error = String((e && (e instanceof Error ? e.message : String(e))) || e);
}

process.stdout.write(JSON.stringify({
    threw,
    error,
    // The whole point: the live <body> must be the SAME OBJECT after the morph, not a replacement.
    sameBody: fromHtml._kids.some((c) => c === liveBody),
    bodyStillAttached: liveBody.parentNode === fromHtml,
    children: fromHtml._kids.map((c) => c.nodeName),
    // And the morph must still have applied the payload's content to it. The <div> lives inside
    // <body>, which is NOT one of the containers that ignores formatting text -- so this also pins
    // that the filter stayed scoped to <html>/<head>.
    bodyText: liveDiv._kids[0]?.nodeValue ?? ""
}) + "\n");
