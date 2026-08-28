// Node-driven fixture for the dirty-field base capture that the redeploy form restore merges against
// (Rask.Core/Resources/rask-morph.js — raskNoteDirtyField / raskDirtyFieldBase / raskFieldBase, read by
// saveRestoreFields / applyRestoreFields in rask.js).
//
// The load-bearing property is scenario 1. A restored edit is only re-applied when the REPLACEMENT
// server renders the same base the old one did, so the base has to be what the server had rendered
// BEFORE the user touched the field. It cannot be read off the DOM at reload time: morph() and the diff
// sync the `value` ATTRIBUTE unconditionally (only the `.value` PROPERTY is guarded), so every echo of
// the user's own keystrokes rewrites it. Capture at first-dirty, and the echo is harmless; capture late,
// and base always equals the user's text, every field compares unequal against a pristine replacement,
// and the whole feature is a silent no-op that still passes a naive test.
//
// The C# test (RestoreFieldBaseTests) runs this in a node subprocess and asserts the single JSON line on
// stdout. Exits non-zero on an internal stub failure.


// ----- Minimal element stub -----
// Attribute and property are kept independent, exactly as a browser keeps them once an input's dirty
// value flag is set — which is the whole distinction under test.
//
// The functions under test arrive by IMPORT. They used to be read off disk and evaluated with
// `new Function(src + "return { … }")`, because the shared modules were bare declarations meant to be
// pasted into a host's scope — there was no other way to reach them, nothing checked that the names
// in that string still existed, and it stops working the moment a module has real `export`s.

import {morph, raskDirtyFieldBase, raskFieldBase, raskIsDirtyField, raskNoteDirtyField, raskNotePendingValue, raskRadioGroup} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, installStubGlobals, type StubControl} from "./stub-dom.js";

const radios: StubControl[] = [];

function makeEl(nodeName: string, attrs?: Record<string, string>, form?: StubControl): StubControl {
    const a = new Map(Object.entries(attrs || {}));
    const el = {
        nodeType: 1,
        nodeValue: null,
        nodeName,
        tagName: nodeName,
        parentNode: null,
        previousSibling: null,
        nextSibling: null,
        firstChild: null,
        textContent: "",
        form: form || null,
        value: a.get("value") ?? "",
        checked: a.has("checked"),
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, String(v)),
        removeAttribute: (n: string) => a.delete(n),
        get attributes() {
            return [...a.entries()].map(([name, value]) => ({name, value}));
        }
    };
    if (nodeName === "INPUT" && a.get("type") === "radio") radios.push(el);
    // One cast, where the fiction is created: a partial fake asserted to be the node it stands
    // in for. Everything the scenarios do with it is then checked against that shape, and the
    // crossing into framework code is marked separately by asDom().
    return el as unknown as StubControl;
}

const makeInput = (attrs: Record<string, string>) => makeEl("INPUT", attrs);
const makeTextarea = (text: string) => {
    const el = makeEl("TEXTAREA", {});
    el.textContent = text;
    el.value = text;
    // One cast, where the fiction is created: a partial fake asserted to be the node it stands
    // in for. Everything the scenarios do with it is then checked against that shape, and the
    // crossing into framework code is marked separately by asDom().
    return el as unknown as StubControl;
};

// A spectator element owns focus, so morph's focus guard lets the value-sync path run — the realistic
// post-blur state, and the one where an unguarded stale frame does damage.
const elsewhere = {nodeType: 1, nodeName: "BODY", tagName: "BODY"};

installStubGlobals({
    activeElement: elsewhere,
    createElement: () => makeInput({}),
    querySelectorAll: (sel) => (sel === "input[type=radio]" ? radios : [])
});


// ---- Scenario 1: the base must survive the server's echo of the user's own typing ----
// Controlled empty field. The user types; the server echoes it back, rewriting the `value` attribute.
const typed = makeInput({"data-rask-on-input": "h4", "value": ""});
raskNoteDirtyField(asDom(typed));                 // captures "" — what the server had rendered
typed.value = "hello";
morph(asDom(typed), asDom(makeInput({"data-rask-on-input": "h4", "value": "hello"})));   // the echo
const baseAfterEcho = raskDirtyFieldBase(asDom(typed));
const attributeAfterEcho = typed.getAttribute("value");

// ---- Scenario 2: capture-once — a second edit must not move the base ----
raskNoteDirtyField(asDom(typed));
typed.value = "hello there";
const baseAfterSecondEdit = raskDirtyFieldBase(asDom(typed));

// ---- Scenario 3: uncontrolled (no `value` attribute) is null, NOT "" ----
// morph deliberately never writes to an uncontrolled input, so "the server rendered nothing" is a
// genuinely different state from "the server rendered empty" and the two must not be flattened.
const uncontrolled = makeInput({"data-rask-on-input": "h5"});
raskNoteDirtyField(asDom(uncontrolled));
const uncontrolledBase = raskDirtyFieldBase(asDom(uncontrolled));
const controlledEmptyBase = raskFieldBase(asDom(makeInput({"value": ""})));

// ---- Scenario 4: a radio's base is its GROUP's selection, scoped to its form owner ----
const formA = makeEl("FORM", {id: "a"});
const formB = makeEl("FORM", {id: "b"});
const planStd = makeEl("INPUT", {"type": "radio", "name": "plan", "value": "std", "checked": ""}, formA);
const planPro = makeEl("INPUT", {"type": "radio", "name": "plan", "value": "pro"}, formA);
// A same-named group in another form must not leak into the first group's base.
makeEl("INPUT", {"type": "radio", "name": "plan", "value": "ent", "checked": ""}, formB);

planStd.checked = true;
raskNoteDirtyField(asDom(planPro));               // the user picks "pro"
planStd.checked = false;
planPro.checked = true;
const radioBase = raskDirtyFieldBase(asDom(planPro));
const radioGroupSize = raskRadioGroup(asDom(planPro)).length;

// ---- Scenario 5: a textarea's base is its text content ----
const notes = makeTextarea("first draft");
raskNoteDirtyField(asDom(notes));
notes.value = "second draft";
const textareaBase = raskDirtyFieldBase(asDom(notes));

// ---- Scenario 6: the restore's guard arming holds off the pristine catch-up frame ----
// Reproduces what restoreOneField does after a reload: arm the value guard with what the REPLACEMENT
// rendered, then write the user's value back. The server's first frame is computed from its pristine
// model — before the converge message lands — and must not wipe the restore. The converge echo then
// releases the guard, and later server-driven changes win as usual.
const restored = makeInput({"data-rask-on-input": "h9", "value": ""});
raskNotePendingValue(asDom(restored), "");        // the replacement's pristine base
restored.value = "hello";
morph(asDom(restored), asDom(makeInput({"data-rask-on-input": "h9", "value": ""})));       // pristine catch-up frame
const afterPristineFrame = restored.value;
morph(asDom(restored), asDom(makeInput({"data-rask-on-input": "h9", "value": "hello"})));  // echo of our converge
const afterConvergeEcho = restored.value;
morph(asDom(restored), asDom(makeInput({"data-rask-on-input": "h9", "value": "server"}))); // a genuine later change
const afterLaterServerChange = restored.value;

// ---- Scenario 7: an untouched field is not a candidate at all ----
const untouched = makeInput({"data-rask-on-input": "h7", "value": "server value"});
const untouchedIsDirty = raskIsDirtyField(asDom(untouched));

process.stdout.write(JSON.stringify({
    baseAfterEcho,            // ""            — the echo did NOT move the base
    attributeAfterEcho,       // "hello"       — ...even though it DID rewrite the attribute
    baseAfterSecondEdit,      // ""            — capture-once
    uncontrolledBase,         // null          — distinct from ""
    controlledEmptyBase,      // ""
    radioBase,                // "std"         — the group's selection, not this element's
    radioGroupSize,           // 2             — the other form's same-named group stayed out
    textareaBase,             // "first draft"
    afterPristineFrame,       // "hello"       — guard held
    afterConvergeEcho,        // "hello"       — echo applied, guard released
    afterLaterServerChange,   // "server"      — the framework didn't pin the user's value forever
    untouchedIsDirty          // false
}) + "\n");
