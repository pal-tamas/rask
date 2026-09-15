// Node-driven fixture for rask-loading.ts — the mark a control carries while its own handler is in flight —
// and for the one place the morph has to know about it.
//
// Timers are faked: the module reads the global setTimeout/clearTimeout at call time, so replacing them
// before any ticket is begun lets the scenarios step time exactly instead of sleeping.
//
// The C# test (LoadingStateTests) runs this in a node subprocess and asserts the JSON line.

import {
    beginLoading,
    endAllLoading,
    endLoading,
    isVisiblyLoading,
    LOADING_DELAY_MS,
    LOADING_HARD_TIMEOUT_MS,
    loadingTarget,
    runtimeOwnsAttr,
} from "../../../src/Rask.Core/Resources/rask-loading.js";
import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, installStubGlobals, type StubNode, type StubParent} from "./stub-dom.js";

// ----- Fake clock ------------------------------------------------------------------

let now = 0;
let nextId = 1;
const timers = new Map<number, {at: number; fn: () => void}>();
const g = globalThis as unknown as {
    setTimeout: (fn: () => void, ms: number) => number;
    clearTimeout: (id: number) => void;
};
g.setTimeout = (fn, ms) => {
    const id = nextId++;
    timers.set(id, {at: now + ms, fn});
    return id;
};
g.clearTimeout = (id) => { timers.delete(id); };

function advance(ms: number): void {
    const until = now + ms;
    for (;;) {
        let due: [number, {at: number; fn: () => void}] | null = null;
        for (const entry of timers) {
            if (entry[1].at <= until && (!due || entry[1].at < due[1].at)) due = entry;
        }
        if (!due) break;
        timers.delete(due[0]);
        now = due[1].at;
        due[1].fn();
    }
    now = until;
}

// ----- Minimal elements ------------------------------------------------------------

function makeEl(tagName: string, attrs?: Record<string, string>): StubParent {
    const a = new Map(Object.entries(attrs || {}));
    const kids: StubNode[] = [];
    const el: StubParent = {
        nodeType: 1, nodeValue: null, nodeName: tagName, tagName, parentNode: null,
        nextSibling: null, previousSibling: null, textContent: "", innerHTML: "",
        get firstChild() { return kids[0] || null; },
        get childNodes() { return kids; },
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => { a.set(n, String(v)); },
        removeAttribute: (n: string) => { a.delete(n); },
        insertBefore(node: StubNode, ref: StubNode | null) {
            const idx = ref === null ? kids.length : kids.indexOf(ref);
            kids.splice(idx < 0 ? kids.length : idx, 0, node);
            node.parentNode = el;
            return node;
        },
        appendChild(node: StubNode) { return el.insertBefore(node, null); },
        removeChild(node: StubNode) { kids.splice(kids.indexOf(node), 1); node.parentNode = null; return node; },
        _kids: kids,
    };
    return el;
}

function inside(parent: StubParent, node: StubParent): StubParent { parent.appendChild(node); return node; }

const dom = (n: StubParent) => asDom<Element>(n);
installStubGlobals({activeElement: null, createElement: (n) => makeEl(String(n).toUpperCase())});

// ---- Which element waits ----
const toolbarOff = makeEl("DIV", {"data-rask-loading": "off"});
const target = {
    button: loadingTarget(dom(makeEl("BUTTON"))) !== null,
    submitInput: loadingTarget(dom(makeEl("INPUT", {type: "submit"}))) !== null,
    textInput: loadingTarget(dom(makeEl("INPUT", {type: "text"}))) !== null,
    div: loadingTarget(dom(makeEl("DIV"))) !== null,
    optedInDiv: loadingTarget(dom(makeEl("DIV", {"data-rask-loading": ""}))) !== null,
    optedOutButton: loadingTarget(dom(makeEl("BUTTON", {"data-rask-loading": "off"}))) !== null,
    buttonUnderOptedOutAncestor: loadingTarget(dom(inside(toolbarOff, makeEl("BUTTON")))) !== null,
    none: loadingTarget(null) !== null,
};

// ---- The delay, then the mark, then its removal ----
const save = makeEl("BUTTON");
const saveTicket = beginLoading(dom(save));
advance(LOADING_DELAY_MS - 1);
const beforeDelay = {loading: save.hasAttribute("data-loading"), visibly: isVisiblyLoading(dom(save))};
advance(1);
const afterDelay = {
    loading: save.hasAttribute("data-loading"),
    busy: save.getAttribute("aria-busy"),
    visibly: isVisiblyLoading(dom(save)),
    owns: runtimeOwnsAttr(dom(save), "data-loading") && runtimeOwnsAttr(dom(save), "aria-busy"),
    ownsOther: runtimeOwnsAttr(dom(save), "class"),
};
endLoading(saveTicket);
endLoading(saveTicket); // twice is harmless
const afterEnd = {loading: save.hasAttribute("data-loading"), busy: save.hasAttribute("aria-busy")};

// ---- A fast handler never shows it ----
const quick = makeEl("BUTTON");
endLoading(beginLoading(dom(quick)));
advance(LOADING_DELAY_MS * 2);
const fastNeverMarked = !quick.hasAttribute("data-loading");

// ---- Two presses: the mark comes off with the LAST ----
const twice = makeEl("BUTTON");
const first = beginLoading(dom(twice));
const second = beginLoading(dom(twice));
advance(LOADING_DELAY_MS);
endLoading(first);
const afterFirstOfTwo = twice.hasAttribute("data-loading");
endLoading(second);
const afterSecondOfTwo = twice.hasAttribute("data-loading");

// ---- A mark the render wrote stays the render's ----
const serverMarked = makeEl("BUTTON", {"data-loading": ""});
const serverTicket = beginLoading(dom(serverMarked));
advance(LOADING_DELAY_MS);
const serverOwned = !runtimeOwnsAttr(dom(serverMarked), "data-loading");
endLoading(serverTicket);
const serverMarkKept = serverMarked.hasAttribute("data-loading");

// ---- The backstop, and a disconnect ----
const hung = makeEl("BUTTON");
beginLoading(dom(hung));
advance(LOADING_HARD_TIMEOUT_MS);
const hardTimeoutCleared = !hung.hasAttribute("data-loading");

const dropA = makeEl("BUTTON");
const dropB = makeEl("BUTTON");
beginLoading(dom(dropA));
beginLoading(dom(dropB));
advance(LOADING_DELAY_MS);
endAllLoading();
const disconnectCleared = !dropA.hasAttribute("data-loading") && !dropB.hasAttribute("data-loading");

// ---- The morph leaves the runtime's mark alone and still removes what the render dropped ----
const liveBtn = makeEl("BUTTON", {class: "btn", title: "old"});
const liveTicket = beginLoading(dom(liveBtn));
advance(LOADING_DELAY_MS);
morph(dom(liveBtn), dom(makeEl("BUTTON", {class: "btn"})));
const morphKeptMark = liveBtn.hasAttribute("data-loading") && liveBtn.getAttribute("aria-busy") === "true";
const morphRemovedDropped = !liveBtn.hasAttribute("title");
endLoading(liveTicket);
morph(dom(liveBtn), dom(makeEl("BUTTON", {class: "btn"})));
const afterEndMorphClean = !liveBtn.hasAttribute("data-loading");

// A render that stops writing its own `data-loading` takes it off: only the runtime's mark is protected.
const renderedMark = makeEl("BUTTON", {"data-loading": ""});
morph(dom(renderedMark), dom(makeEl("BUTTON")));
const morphRemovedRenderedMark = !renderedMark.hasAttribute("data-loading");

process.stdout.write(JSON.stringify({
    target,
    beforeDelay,
    afterDelay,
    afterEnd,
    fastNeverMarked,
    afterFirstOfTwo,
    afterSecondOfTwo,
    serverOwned,
    serverMarkKept,
    hardTimeoutCleared,
    disconnectCleared,
    morphKeptMark,
    morphRemovedDropped,
    afterEndMorphClean,
    morphRemovedRenderedMark,
}) + "\n");
