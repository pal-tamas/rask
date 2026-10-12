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
    loadingScope,
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

// ---- A scope: what is around the press waits with it ----
const pager = makeEl("DIV", {"data-rask-loading": "within"});
const pageTwo = inside(inside(pager, makeEl("DIV")), makeEl("BUTTON"));
const offAbove = makeEl("SECTION", {"data-rask-loading": "off"});
const pagerUnderOff = inside(offAbove, makeEl("DIV", {"data-rask-loading": "within"}));
const offBetween = inside(makeEl("DIV", {"data-rask-loading": "within"}), makeEl("DIV", {"data-rask-loading": "off"}));
const scopeOf = {
    descendant: loadingScope(dom(pageTwo)) === dom(pager),
    // The element that asks is a scope for what is inside it, not for itself.
    itself: loadingScope(dom(pager)) === null,
    none: loadingScope(dom(makeEl("BUTTON"))) === null && loadingScope(null) === null,
    underOptOut: loadingScope(dom(inside(pagerUnderOff, makeEl("BUTTON")))) === null,
    optOutBetween: loadingScope(dom(inside(offBetween, makeEl("BUTTON")))) === null,
    nothingToMark: beginLoading(null, dom(makeEl("A"))) === null,
};

const pageTicket = beginLoading(loadingTarget(dom(pageTwo)), dom(pageTwo));
advance(LOADING_DELAY_MS - 1);
const scopeBeforeDelay = pager.hasAttribute("data-loading");
advance(1);
const scopeAfterDelay = {
    loading: pager.hasAttribute("data-loading"),
    busy: pager.hasAttribute("aria-busy"),
    control: pageTwo.hasAttribute("data-loading") && pageTwo.getAttribute("aria-busy") === "true",
    // Only the pressed control refuses a second press: every other button in the pager stays pressable.
    visibly: isVisiblyLoading(dom(pager)),
    owns: runtimeOwnsAttr(dom(pager), "data-loading"),
    ownsBusy: runtimeOwnsAttr(dom(pager), "aria-busy"),
};
// The render that answers the press replaces the pressed number and writes no mark on the pager.
pageTwo.parentNode!.removeChild(pageTwo);
morph(dom(pager), dom(makeEl("DIV", {"data-rask-loading": "within"})));
const scopeMarkSurvivesTheRender = pager.hasAttribute("data-loading");
endLoading(pageTicket);
const scopeAfterEnd = pager.hasAttribute("data-loading");

// A link or a row with a handler is not a control, and still makes what is around it wait.
const sorter = makeEl("DIV", {"data-rask-loading": "within"});
const plainOwner = inside(sorter, makeEl("A"));
const plainTicket = beginLoading(loadingTarget(dom(plainOwner)), dom(plainOwner));
advance(LOADING_DELAY_MS);
const scopeMarkedByAPlainOwner = sorter.hasAttribute("data-loading") && !plainOwner.hasAttribute("data-loading");
endLoading(plainTicket);

// Two presses inside one scope: the mark comes off with the last.
const busyPager = makeEl("DIV", {"data-rask-loading": "within"});
const pressOne = beginLoading(null, dom(inside(busyPager, makeEl("A"))));
const pressTwo = beginLoading(null, dom(inside(busyPager, makeEl("A"))));
advance(LOADING_DELAY_MS);
endLoading(pressOne);
const scopeAfterFirstOfTwo = busyPager.hasAttribute("data-loading");
endLoading(pressTwo);
const scopeAfterSecondOfTwo = busyPager.hasAttribute("data-loading");

// A scope the render marked itself is the render's, as a control's is.
const renderedScope = makeEl("DIV", {"data-rask-loading": "within", "data-loading": ""});
const renderedScopeTicket = beginLoading(null, dom(inside(renderedScope, makeEl("A"))));
advance(LOADING_DELAY_MS);
endLoading(renderedScopeTicket);
const renderedScopeMarkKept = renderedScope.hasAttribute("data-loading");

process.stdout.write(JSON.stringify({
    scopeOf,
    scopeBeforeDelay,
    scopeAfterDelay,
    scopeMarkSurvivesTheRender,
    scopeAfterEnd,
    scopeMarkedByAPlainOwner,
    scopeAfterFirstOfTwo,
    scopeAfterSecondOfTwo,
    renderedScopeMarkKept,
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
