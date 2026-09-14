// Node-driven fixture for the shared diff interpreter's PATH RESOLUTION
// (Rask.Core/Resources/rask-dom.ts `resolvePath` → `relevantChild`).
//
// Reproduces #1097: a WASM page's click reached .NET, the handler ran, and a one-op diff frame
// arrived — `[[3,[1,1,0,1,0],"clicks=1"]]` — yet the page kept showing `clicks=0`, with no console
// error and no page error.
//
// The page was served with `</head>\n<body data-rask-root>`. Per the HTML parser's "after head"
// insertion mode that newline becomes a text node INSIDE <html>, so the live <html> holds
// [HEAD, #text, BODY]. The server's frame walk counts [HEAD, BODY] and addresses BODY as slot 1.
//
// Since c3ddc923 the MORPH ignores that text node when pairing <html>'s children (see
// PrerenderWhitespaceMorphFixture) — which is exactly why it survives hydration. The path walk still
// counted it, so slot 1 resolved to the newline, the next step asked a text node for a child, got
// null, and UpdateText's `if (textNode)` dropped the op in silence. Every in-place update on every
// WASM page served from such a shell — the shipped `wasm` template's included — was lost this way.
//
// Two scenarios, one JSON line:
//   html  the bug: an UpdateText through a formatting newline in <html> must reach its text node.
//   body  the scope: whitespace under <body> IS rendered, so it must still be counted — a filter
//         that leaked past <html>/<head> would shift real, visible nodes instead.
//
// The C# test (DiffPathFormattingTextTests) runs this and asserts the line.

import {applyDiff} from "../../../src/Rask.Core/Resources/rask-dom.js";
import {installStubGlobals, type StubNode, type StubParent} from "./stub-dom.js";

let reloaded = false;
(globalThis as unknown as { location: unknown }).location = {reload: () => { reloaded = true; }};
// Deliberately NO MutationObserver and NO document.addEventListener: rask-dom.ts's install* IIFEs
// then early-return on load, so the fixture exercises only applyDiff's path resolution.

function makeText(value: string): StubNode {
    // Both fields, because the framework reads both on the same node: the morph compares
    // `nodeValue`, the diff codec assigns `textContent`.
    return {
        nodeType: 3, nodeName: "#text", nodeValue: value, textContent: value,
        parentNode: null, nextSibling: null, previousSibling: null, firstChild: null,
    };
}

function makeNode(nodeType: number, nodeName: string, children: StubNode[] = []): StubParent {
    const kids: StubNode[] = [];
    const attrs = new Map<string, string>();
    const el: StubParent = {
        nodeType, nodeName, tagName: nodeName, nodeValue: null, textContent: "",
        parentNode: null, nextSibling: null, previousSibling: null, innerHTML: "",
        get firstChild() { return kids[0] || null; },
        get childNodes() { return kids; },
        get attributes() { return [...attrs.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n: string) => attrs.has(n),
        getAttribute: (n: string) => attrs.get(n) ?? null,
        setAttribute: (n: string, v: string) => { attrs.set(n, String(v)); },
        removeAttribute: (n: string) => { attrs.delete(n); },
        insertBefore(node: StubNode, ref: StubNode | null) {
            const idx = ref === null ? kids.length : kids.indexOf(ref);
            kids.splice(idx < 0 ? kids.length : idx, 0, node);
            node.parentNode = el;
            return node;
        },
        appendChild(node: StubNode) { return el.insertBefore(node, null); },
        removeChild(node: StubNode) {
            kids.splice(kids.indexOf(node), 1);
            node.parentNode = null;
            return node;
        },
        _kids: kids,
    };
    for (const c of children) el.appendChild(c);
    return el;
}

const element = (name: string, children: StubNode[] = []) => makeNode(1, name, children);

/** A document holding [<!DOCTYPE>, <html>], which is what resolvePath's first slot counts. */
function documentOf(html: StubParent): { activeElement: null; childNodes: StubNode[] } {
    return {activeElement: null, childNodes: [makeNode(10, "html"), html]};
}

function apply(doc: { activeElement: null; childNodes: StubNode[] }, ops: unknown[]): { threw: boolean; error: string } {
    installStubGlobals(doc);
    try {
        applyDiff(ops as Parameters<typeof applyDiff>[0]);
        return {threw: false, error: ""};
    } catch (e) {
        return {threw: true, error: e instanceof Error ? (e.stack ?? e.message) : String(e)};
    }
}

// ----- html: the #1097 shape, verbatim ------------------------------------------------------
// <html>[HEAD, #text("\n"), BODY[MAIN[H1, P["clicks=0"], BUTTON]]]
const htmlCounter = makeText("clicks=0");
const htmlDoc = documentOf(element("HTML", [
    element("HEAD", [element("TITLE")]),
    makeText("\n"),
    element("BODY", [element("MAIN", [element("H1"), element("P", [htmlCounter]), element("BUTTON")])]),
]));
const htmlRun = apply(htmlDoc, [[3, [1, 1, 0, 1, 0], "clicks=1"]]);

// ----- body: whitespace under <body> is rendered, so it is still a slot ---------------------
// <html>[HEAD, BODY[#text(" "), MAIN[P["v1"]]]]  — MAIN is slot 1 under BODY. No newline in <html>
// here, so this scenario passes with or without the fix and fails only for a filter that is too broad.
const bodyCounter = makeText("v1");
const bodyDoc = documentOf(element("HTML", [
    element("HEAD"),
    element("BODY", [makeText(" "), element("MAIN", [element("P", [bodyCounter])])]),
]));
const bodyRun = apply(bodyDoc, [[3, [1, 1, 1, 0, 0], "v2"]]);

process.stdout.write(JSON.stringify({
    reloaded,
    html: {...htmlRun, text: htmlCounter.textContent},
    body: {...bodyRun, text: bodyCounter.textContent},
}) + "\n");
