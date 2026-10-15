// Node-driven fixture for rask-submitting.ts — what a form does while its submit is in flight — and for the
// one place the morph has to know about it.
//
// Timers are faked as in LoadingStateFixture: the hard timeout is stepped, not slept. The C# test
// (FormSubmittingTests) runs this in a node subprocess and asserts the JSON line.

import {LOADING_HARD_TIMEOUT_MS} from "../../../src/Rask.Core/Resources/rask-loading.js";
import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {ownsAttr} from "../../../src/Rask.Core/Resources/rask-owned.js";
import {
    holdForm,
    isSubmitting,
    releaseAllForms,
    releaseForm,
} from "../../../src/Rask.Core/Resources/rask-submitting.js";
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
    for (const [id, t] of [...timers].sort((a, b) => a[1].at - b[1].at)) {
        if (t.at > until || !timers.has(id)) continue;
        timers.delete(id);
        now = t.at;
        t.fn();
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

// A form and its controls as `form.elements` lists them: a button outside it with `form=` is one of them.
function makeForm(...controls: StubParent[]): StubParent {
    const form = makeEl("FORM");
    (form as unknown as {elements: StubParent[]}).elements = controls;
    return form;
}

const dom = (n: StubParent) => asDom<Element>(n);
installStubGlobals({activeElement: null, createElement: (n) => makeEl(String(n).toUpperCase())});

// ---- At the press: submit buttons disabled, fields readonly, the rest untouched ----
const save = makeEl("BUTTON");
const typedSubmit = makeEl("BUTTON", {type: "submit"});
const imageSubmit = makeEl("INPUT", {type: "image"});
const outside = makeEl("BUTTON", {form: "f"});
const plain = makeEl("BUTTON", {type: "button"});
const name = makeEl("INPUT", {type: "text"});
const untyped = makeEl("INPUT");
const notes = makeEl("TEXTAREA");
const hidden = makeEl("INPUT", {type: "hidden"});
const box = makeEl("INPUT", {type: "checkbox"});
const list = makeEl("SELECT");
const form = makeForm(save, typedSubmit, imageSubmit, outside, plain, name, untyped, notes, hidden, box, list);
const before = isSubmitting(dom(form));
const hold = holdForm(dom(form));
const atPress = {
    submitting: isSubmitting(dom(form)),
    submitButtonsDisabled: [save, typedSubmit, imageSubmit, outside].every((b) => b.hasAttribute("disabled")),
    fieldsReadonly: [name, untyped, notes].every((f) => f.hasAttribute("readonly")),
    plainButtonLive: !plain.hasAttribute("disabled"),
    restUntouched: [hidden, box, list].every((c) => !c.hasAttribute("readonly") && !c.hasAttribute("disabled")),
    owned: ownsAttr(dom(save), "disabled") && ownsAttr(dom(name), "readonly"),
};

// ---- A second submit is refused ----
const second = holdForm(dom(form));

// ---- The render the submit causes lands mid-flight: it neither hands the button back nor is forgotten ----
morph(dom(save), dom(makeEl("BUTTON", {class: "primary"})));
morph(dom(typedSubmit), dom(makeEl("BUTTON", {type: "submit", disabled: ""})));
const midFlight = {
    stillDisabled: save.hasAttribute("disabled"),
    restRendered: save.getAttribute("class") === "primary",
};

// ---- Done: each control gets what the render last said ----
releaseForm(hold);
releaseForm(hold); // twice is harmless
const afterRelease = {
    submitting: isSubmitting(dom(form)),
    buttonsBack: [save, imageSubmit, outside].every((b) => !b.hasAttribute("disabled")),
    renderedDisabledKept: typedSubmit.hasAttribute("disabled"),
    fieldsBack: [name, untyped, notes].every((f) => !f.hasAttribute("readonly")),
    disowned: !ownsAttr(dom(save), "disabled") && !ownsAttr(dom(name), "readonly"),
};
const again = holdForm(dom(form));
const takesTheNextSubmit = again !== null;
releaseForm(again);

// ---- What the render wrote before the press stays the render's ----
const alreadyOff = makeEl("BUTTON", {disabled: ""});
const alreadyReadonly = makeEl("INPUT", {readonly: ""});
releaseForm(holdForm(dom(makeForm(alreadyOff, alreadyReadonly))));
const renderedKept = alreadyOff.hasAttribute("disabled") && alreadyReadonly.hasAttribute("readonly");

// ---- A field the render asks to have corrected mid-flight is handed back; the rest stay held ----
const fixButton = makeEl("BUTTON");
const wrong = makeEl("INPUT", {type: "number"});
const right = makeEl("INPUT", {type: "text"});
const fixHold = holdForm(dom(makeForm(fixButton, wrong, right)));
morph(dom(wrong), dom(makeEl("INPUT", {type: "number", "data-rask-on-edit": "7"})));
morph(dom(right), dom(makeEl("INPUT", {type: "text"})));
const handedBack = {
    wrongEditable: !wrong.hasAttribute("readonly") && !ownsAttr(dom(wrong), "readonly"),
    rightHeld: right.hasAttribute("readonly"),
    buttonHeld: fixButton.hasAttribute("disabled"),
};
releaseForm(fixHold);
handedBack.wrongEditable &&= !wrong.hasAttribute("readonly");
const renderedReadonly = makeEl("INPUT", {type: "text"});
const keepHold = holdForm(dom(makeForm(renderedReadonly)));
morph(dom(renderedReadonly), dom(makeEl("INPUT", {type: "text", readonly: "", "data-rask-on-edit": "8"})));
releaseForm(keepHold);
const handedBackKeepsRenderedReadonly = renderedReadonly.hasAttribute("readonly");

// ---- A submit that never reports back, and a dropped connection, both let go ----
const hungButton = makeEl("BUTTON");
const hung = makeForm(hungButton);
holdForm(dom(hung));
advance(LOADING_HARD_TIMEOUT_MS - 1);
const hungHeld = isSubmitting(dom(hung));
advance(1);
const hardTimeoutReleased = !isSubmitting(dom(hung)) && !hungButton.hasAttribute("disabled");

const dropA = makeForm(makeEl("BUTTON"));
const dropB = makeForm(makeEl("BUTTON"));
holdForm(dom(dropA));
holdForm(dom(dropB));
releaseAllForms();
const disconnectReleased = !isSubmitting(dom(dropA)) && !isSubmitting(dom(dropB));

process.stdout.write(JSON.stringify({
    before,
    atPress,
    secondRefused: second === null,
    midFlight,
    afterRelease,
    takesTheNextSubmit,
    renderedKept,
    handedBack,
    handedBackKeepsRenderedReadonly,
    hungHeld,
    hardTimeoutReleased,
    disconnectReleased,
}) + "\n");
