// Node-driven fixture for rask-autofocus.ts — which `autofocus` element a frame that was just applied gives
// focus to, and when it gives it to none.
//
// The module reads `document` once, as it loads, and takes note of the `autofocus` elements the document was
// served with. So the stub document is installed first and the module is imported after it, and every scenario
// then changes the same page, in the order a session would.
//
// The C# test (AutofocusTests) runs this in a node subprocess and asserts the JSON line.

import {installStubGlobals} from "./stub-dom.js";

interface Field {
    id: string;
    tagName: string;
    type: string;
    disabled: boolean;
    isContentEditable: boolean;
    autofocus: boolean;
    shown: boolean;
    inert: boolean;
    closest(selector: string): Field | null;
    getClientRects(): unknown[];
    focus(options?: {preventScroll?: boolean}): void;
}

const page: Field[] = [];
const loaded: (() => void)[] = [];
let kept: boolean | undefined;

const doc = {
    activeElement: null as Field | null,
    querySelectorAll: (selector: string): Field[] => selector === "[autofocus]" ? page.filter(f => f.autofocus) : [],
    addEventListener: (type: string, heard: () => void) => { if (type === "DOMContentLoaded") loaded.push(heard); },
};

function field(id: string, tagName = "INPUT", type = "text"): Field {
    const el: Field = {
        id, tagName, type, disabled: false, isContentEditable: false, autofocus: true, shown: true, inert: false,
        closest: (selector) => selector === "[inert]" && el.inert ? el : null,
        getClientRects: () => el.shown ? [{}] : [],
        focus(options) {
            doc.activeElement = el;
            kept = !!options && options.preventScroll === true;
        },
    };
    return el;
}

function add(...fields: Field[]): void { page.push(...fields); }
function clear(): void { page.length = 0; doc.activeElement = null; kept = undefined; }
const focus = () => doc.activeElement ? doc.activeElement.id : null;

// ---- The document as it was served ----
const served = field("served");
add(served);
installStubGlobals(doc);
const {focusArrived} = await import("../../../src/Rask.Core/Resources/rask-autofocus.js");
const parsedLater = field("parsed-later");
add(parsedLater);
loaded.forEach(heard => heard());
const servedTook = focusArrived();
const servedFocus = focus();

// ---- A navigation ----
clear();
const hidden = field("hidden"); hidden.shown = false;
const off = field("off"); off.disabled = true;
const inert = field("inert"); inert.inert = true;
add(hidden, off, inert, served, field("second"));
const navigationTook = focusArrived({action: "push"});
const navigationFocus = focus();
const navigationScrolls = kept === false;

clear();
const noneTook = focusArrived({action: "push"});
const noneFocus = focus();

// ---- At a fragment: noted, and never owed ----
clear();
const atFragment = field("at-fragment");
add(atFragment);
const fragmentTook = focusArrived({action: "push"}, true);
const fragmentFocus = focus();
const fragmentLaterTook = focusArrived();

// ---- The page the reader is on ----
clear();
const button = field("button", "BUTTON", "submit"); button.autofocus = false;
doc.activeElement = button;
const row = field("row");
add(row);
const arrivalTook = focusArrived();
const arrivalFocus = focus();
const arrivalScrolls = kept === false;
doc.activeElement = button;
const againTook = focusArrived();
const againFocus = focus();

function whileOn(tagName: string, type: string, editable = false): string | null {
    const on = field("on", tagName, type);
    on.autofocus = false;
    on.isContentEditable = editable;
    doc.activeElement = on;
    add(field("arrives-" + page.length));
    focusArrived();
    return focus();
}

const whileTyping = {
    text: whileOn("INPUT", "text"),
    search: whileOn("INPUT", "search"),
    textarea: whileOn("TEXTAREA", "textarea"),
    select: whileOn("SELECT", "select-one"),
    editable: whileOn("DIV", "", true),
};
const whileNotTyping = {
    checkbox: (whileOn("INPUT", "checkbox") ?? "").startsWith("arrives-"),
    radio: (whileOn("INPUT", "radio") ?? "").startsWith("arrives-"),
    submit: (whileOn("INPUT", "submit") ?? "").startsWith("arrives-"),
    link: (whileOn("A", "") ?? "").startsWith("arrives-"),
};
// What the reader was typing over is not focused by the next render either.
doc.activeElement = button;
const afterTypingTook = focusArrived();

// ---- Not on screen as it arrives: a closed dialog's field is the browser's to focus when it opens ----
clear();
doc.activeElement = button;
const inDialog = field("in-dialog"); inDialog.shown = false;
add(inDialog);
const closedTook = focusArrived();
inDialog.shown = true;
const openedTook = focusArrived();
const openedFocus = focus();

// ---- Back: a replaced entry ----
clear();
doc.activeElement = button;
const back = field("back");
add(served, back);
const backTook = focusArrived({action: "replace"});
const backFocus = focus();
const backKeepsScroll = kept === true;

console.log(JSON.stringify({
    served: {took: servedTook, focus: servedFocus},
    navigation: {took: navigationTook, focus: navigationFocus, scrolls: navigationScrolls},
    none: {took: noneTook, focus: noneFocus},
    fragment: {took: fragmentTook, focus: fragmentFocus, laterTook: fragmentLaterTook},
    arrival: {took: arrivalTook, focus: arrivalFocus, scrolls: arrivalScrolls, againTook, againFocus},
    whileTyping,
    whileNotTyping,
    afterTypingTook,
    closed: {took: closedTook, openedTook, openedFocus},
    back: {took: backTook, focus: backFocus, keepsScroll: backKeepsScroll},
}));
