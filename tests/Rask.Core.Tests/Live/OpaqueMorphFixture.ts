// Node-driven fixture for the external-component diff boundary in the shared client morph
// (Rask.Core/Resources/rask-morph.ts — the data-rask-opaque early return).
//
// The scenario is the one that silently corrupts. What a foreign renderer put in the DOM and what the
// server thinks is there permanently disagree: the children below the host were created in the
// browser by React/Lit/Blazor after mount, and the server's HTML either has none of them or still
// carries the <template data-rask-slot> the client lifted out and deleted. So on any full-document
// morph — scoped-CSS delivery, a reconnect, any untrusted structural op — a positional walk trims
// every mounted node, and the component goes blank until something re-mounts it.
//
// Attributes must still sync across the boundary: that is how a changed `props` reaches the adapter.
//
// The C# test (OpaqueMorphTests) runs this in a node subprocess and asserts the JSON on stdout.
//
// The functions under test arrive by IMPORT, matching every other fixture here: they used to be read
// off disk and evaluated in a string, which stopped working the moment the shared modules had real
// `export`s.

import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, installStubGlobals, type StubNode, type StubParent} from "./stub-dom.js";

installStubGlobals({
    activeElement: null,
    createElement: (name) => makeEl(String(name).toUpperCase())
});

// ----- Minimal element stub, same shape the sibling fixtures use -----
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
        textContent: text ?? "",
        innerHTML: "",
        get firstChild() { return kids[0] || null; },
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, v),
        removeAttribute: (n: string) => a.delete(n),
        insertBefore(node: StubNode, ref: StubNode | null) {
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

function withKids(nodeName: string, children: StubNode[], attrs?: Record<string, string>): StubParent {
    const el = makeEl(nodeName, attrs);
    for (const c of children) el.appendChild(c);
    return el;
}

/** A live host: server-rendered attributes, browser-created children. */
function liveHost(props: string, opaque: boolean): StubParent {
    const attrs: Record<string, string> = {name: "Chart", props};
    if (opaque) attrs["data-rask-opaque"] = "";
    return withKids("RASK-EXTERNAL", [
        withKids("DIV", [makeEl("SVG"), makeEl("SPAN", {}, "41,200")], {class: "recharts-wrapper"})
    ], attrs);
}

/** What the server re-renders: the same host, new props, and no children of its own. */
function renderedHost(props: string, opaque: boolean): StubParent {
    const attrs: Record<string, string> = {name: "Chart", props};
    if (opaque) attrs["data-rask-opaque"] = "";
    return makeEl("RASK-EXTERNAL", attrs);
}

function run(opaque: boolean) {
    const from = withKids("DIV", [liveHost('{"total":1}', opaque)]);
    const to = withKids("DIV", [renderedHost('{"total":2}', opaque)]);

    let threw = false;
    let error = "";
    try {
        morph(asDom(from), asDom(to));
    } catch (e) {
        threw = true;
        error = String((e && (e instanceof Error ? e.message : String(e))) || e);
    }

    const host = from._kids[0] as StubParent | undefined;
    return {
        threw,
        error,
        // Survivors of the host's own subtree — the thing the boundary protects.
        survivingChildren: host ? host._kids.map((c) => c.nodeName) : [],
        // Props must cross the boundary even though children do not.
        props: host ? host.getAttribute("props") : null
    };
}

process.stdout.write(JSON.stringify({
    opaque: run(true),
    // Negative control: identical shapes with the marker off. If this one ALSO keeps its children the
    // fixture is proving nothing, because the morph never wanted to remove them in the first place.
    transparent: run(false)
}) + "\n");
