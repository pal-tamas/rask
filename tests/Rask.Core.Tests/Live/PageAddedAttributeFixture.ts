// Node-driven fixture for the morph's rule on attributes the page added itself (rask-written.ts): a render takes away
// only what the server wrote. The C# test (PageAddedAttributeTests) runs this in a node subprocess and reads the JSON
// line. Node has no MutationObserver, so what the watch would hear is noted by hand with notePageAdded.

import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {own} from "../../../src/Rask.Core/Resources/rask-owned.js";
import {notePageAdded} from "../../../src/Rask.Core/Resources/rask-written.js";
import {asDom, installStubGlobals, type StubParent} from "./stub-dom.js";

// A childless element: the rule is about attributes, and a morph of one without children pairs nothing below it.
function makeEl(nodeName: string, attrs?: Record<string, string>): StubParent {
    const a = new Map(Object.entries(attrs || {}));
    return {
        nodeType: 1, nodeValue: null, nodeName, tagName: nodeName, parentNode: null,
        nextSibling: null, previousSibling: null, textContent: "", innerHTML: "", firstChild: null, _kids: [],
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, String(v)),
        removeAttribute: (n: string) => a.delete(n),
        insertBefore: (node) => node,
        appendChild: (node) => node,
        removeChild: (node) => node,
    };
}

installStubGlobals({activeElement: null, head: makeEl("HEAD")});

// The page sized the canvas; renders that never wrote a size leave it alone, and the one the server wrote goes.
const canvas = makeEl("CANVAS", {class: "ground", title: "ground", "aria-hidden": "true"});
canvas.setAttribute("width", "640");
notePageAdded(asDom(canvas), "width");
morph(asDom(canvas), asDom(makeEl("CANVAS", {class: "ground", "aria-hidden": "true"})));
morph(asDom(canvas), asDom(makeEl("CANVAS", {class: "ground moved", "aria-hidden": "true"})));
const sizeKept = canvas.getAttribute("width") === "640";
const titleRemoved = !canvas.hasAttribute("title");

// Once a render has written the attribute it is the server's, and a render that leaves it out takes it away.
const box = makeEl("DIV");
box.setAttribute("data-mode", "mine");
notePageAdded(asDom(box), "data-mode");
morph(asDom(box), asDom(makeEl("DIV", {"data-mode": "server"})));
const serverValue = box.getAttribute("data-mode");
morph(asDom(box), asDom(makeEl("DIV")));
const writtenThenRemoved = !box.hasAttribute("data-mode");

// A server-written data-rask-* hook attribute goes as any other; one a hook owns stays whoever wrote it.
const button = makeEl("BUTTON", {"data-rask-on-click": "h1", "aria-expanded": "false"});
own(asDom(button), "aria-expanded");
morph(asDom(button), asDom(makeEl("BUTTON")));
const hookAttributeRemoved = !button.hasAttribute("data-rask-on-click");
const ownedKept = button.getAttribute("aria-expanded") === "false";

process.stdout.write(JSON.stringify({
    sizeKept,              // true — the page's width survives two renders that never wrote one
    titleRemoved,          // true — the title the server wrote and dropped is gone
    serverValue,           // "server" — a render writing the attribute takes it over
    writtenThenRemoved,    // true — and a later render that leaves it out removes it
    hookAttributeRemoved,  // true — a dropped data-rask-on-click is removed like any server attribute
    ownedKept,             // true — an owned attribute is the hook's, as before
}) + "\n");
