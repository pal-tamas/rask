// Node-driven fixture for rask-morph.js's data-rask-managed handling (both sides of the
// child-reconciliation filter).
//
// data-rask-managed marks nodes that are live but ABSENT from the .NET render payload (a
// library's injected DOM, the reconnect overlay, the WASM bundle tags). morph filters them
// out of BOTH the existing (from) and incoming (to) child lists:
//   * from-side (always correct): a marked node the .NET tree doesn't know about is preserved,
//     not paired against an incoming child and trimmed/replaced.
//   * to-side (the guard): a marked node that IS in the incoming payload is a contradiction —
//     a rendered node is by definition part of the payload. Before the guard, the from copy was
//     filtered out but the to copy wasn't, so every morph appended a fresh unpaired duplicate
//     (the playground's unbounded empty .pg-code-host growth, issue #419). The guard skips it,
//     turning the misuse into a harmless no-op.
//
// The C# test (MorphManagedGuardTests) runs this in a node subprocess and asserts the JSON line.


// ----- Minimal DOM stub (child list with sibling relinking) -----------------------
//
// The functions under test arrive by IMPORT. They used to be read off disk and evaluated with
// `new Function(src + "return { … }")`, because the shared modules were bare declarations meant to be
// pasted into a host's scope — there was no other way to reach them, nothing checked that the names
// in that string still existed, and it stops working the moment a module has real `export`s.

import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, asStubParent, installStubGlobals, type StubNode, type StubParent} from "./stub-dom.js";

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
        // Declared because StubParent does; this fixture never morphs a subtree.
        innerHTML: "",
        get firstChild() { return kids[0] || null; },
        get childNodes() { return kids; },
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, String(v)),
        removeAttribute: (n: string) => a.delete(n),
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
    // One cast, where the fiction is created: a partial fake asserted to be the node it stands
    // in for. Everything the scenarios do with it is then checked against that shape, and the
    // crossing into framework code is marked separately by asDom().
    return el;
}

function child(parent: StubParent, node: StubParent): StubParent { parent.appendChild(node); return node; }

installStubGlobals({activeElement: null, head: makeEl("HEAD"), createElement: (n) => makeEl(String(n).toUpperCase())});


// ---- Scenario A: the MISUSE (marker on the host the .NET side renders) fails safe ----
// from: <section.pg-editor> → <div.pg-code-host data-rask-managed> → <canvas> (Monaco stand-in)
// to:   <section.pg-editor> → <div.pg-code-host data-rask-managed> (childless, freshly rendered)
// Before the to-side guard this appended a second empty host every morph; now it's a no-op.
const editorFrom = makeEl("SECTION", {class: "pg-editor"});
const hostFrom = child(editorFrom, makeEl("DIV", {class: "pg-code-host", "data-rask-managed": ""}));
child(hostFrom, makeEl("CANVAS", {}));               // Monaco's DOM
const editorTo = makeEl("SECTION", {class: "pg-editor"});
child(editorTo, makeEl("DIV", {class: "pg-code-host", "data-rask-managed": ""}));

morph(asDom(editorFrom), asDom(editorTo));
morph(asDom(editorFrom), asDom(editorTo));                          // a second frame must not duplicate either
const misuseHostCount = editorFrom._kids.filter((k: StubNode) => asStubParent(k).getAttribute("class") === "pg-code-host").length;
const misuseMonacoKept = hostFrom.parentNode === editorFrom
    && hostFrom._kids.some((k: StubNode) => k.nodeName === "CANVAS");

// ---- Scenario B: the CORRECT placement (marker on the library-created child) survives ----
// from: <section.pg-editor> → <div.pg-code-host> (unmarked host) → <canvas data-rask-managed>
// to:   <section.pg-editor> → <div.pg-code-host> (unmarked, childless — what the .NET side renders)
// The host pairs and recurses; the marked child is filtered out of the host's from-side, so a
// childless incoming host does NOT strip Monaco.
const editorFrom2 = makeEl("SECTION", {class: "pg-editor"});
const hostFrom2 = child(editorFrom2, makeEl("DIV", {class: "pg-code-host"}));
child(hostFrom2, makeEl("CANVAS", {"data-rask-managed": ""}));
const editorTo2 = makeEl("SECTION", {class: "pg-editor"});
child(editorTo2, makeEl("DIV", {class: "pg-code-host"}));

morph(asDom(editorFrom2), asDom(editorTo2));
const correctHostCount = editorFrom2._kids.filter((k: StubNode) => asStubParent(k).getAttribute("class") === "pg-code-host").length;
const correctMonacoKept = hostFrom2._kids.some((k: StubNode) => k.nodeName === "CANVAS");

process.stdout.write(JSON.stringify({
    misuseHostCount,     // 1 — no duplicate empty host appended (was 3 after two frames pre-guard)
    misuseMonacoKept,    // true — the original host and its Monaco DOM untouched
    correctHostCount,    // 1 — single host
    correctMonacoKept    // true — marked child survives a childless incoming host
}) + "\n");
