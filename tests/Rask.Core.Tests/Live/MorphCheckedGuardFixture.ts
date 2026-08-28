// Node-driven fixture for the shared client's `.checked` echo guard
// (raskNotePendingChecked / raskShouldSuppressChecked in rask-morph.js, consumed
// by both the full morph (rask-morph.js) and the diff codec (rask-dom.js's
// syncFormProperty), which in turn feed rask.js and rask.wasm.js).
//
// Reproduces the radio/checkbox desync that turned the Forms guide's radio-group
// step red on the standalone WASM host: a user clicks a radio (browser flips the
// `.checked` PROPERTY natively, leaving the `checked` ATTRIBUTE untouched), and a
// re-render the server computed BEFORE that click reached it lands afterwards.
// Pre-fix, both apply paths set `.checked` unconditionally, reverting the click —
// Playwright then reports "Clicking the checkbox did not change its state". The
// guard records the pre-click attribute state on the change dispatch and suppresses
// a frame that still carries it, until an authoritative frame (the echo of the new
// state) arrives with a different value and releases the guard.
//
// The C# test (MorphCheckedGuardTests) runs this in a node subprocess and asserts
// the single JSON line on stdout. Exits non-zero on an internal stub failure.
//
// The functions under test arrive by IMPORT. They used to be read off disk and evaluated with
// `new Function(src + "return { … }")`, because the shared modules were bare declarations meant to be
// pasted into a host's scope — there was no other way to reach them, nothing checked that the names
// in that string still existed, and it stops working the moment a module has real `export`s.

import {morph, raskNotePendingChecked} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {syncFormProperty} from "../../../src/Rask.Core/Resources/rask-dom.js";
import {asDom, installStubGlobals, type StubControl} from "./stub-dom.js";


// ----- Minimal element stub -----
// Models a real input AFTER a native click: the `checked` attribute (the last
// server-rendered default) and the live `.checked` property are independent —
// clicking flips the property but never the attribute.
function makeInput(attrs: Record<string, string>, checked?: boolean): StubControl {
    const a = new Map(Object.entries(attrs || {}));
    // One cast, where the fiction is created: a partial fake asserted to be the node it stands
    // in for. Everything the scenarios do with it is then checked against that shape, and the
    // crossing into framework code is marked separately by asDom().
    return {
        nodeType: 1,
        nodeValue: null,
        nodeName: "INPUT",
        tagName: "INPUT",
        parentNode: null,
        previousSibling: null,
        nextSibling: null,
        firstChild: null,
        textContent: "",
        value: a.get("value") ?? "",
        checked: !!checked,
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, String(v)),
        removeAttribute: (n: string) => a.delete(n),
        get attributes() {
            return [...a.entries()].map(([name, value]) => ({name, value}));
        }
    };
}

// A spectator element owns focus so the morph value/checked path runs (the
// realistic post-click state — focus is on the just-clicked control's group,
// never mid-typing here).
const elsewhere = {nodeType: 1, nodeName: "BODY", tagName: "BODY"};
installStubGlobals({
    activeElement: elsewhere,
    createElement: () => makeInput({})
});


// Radio option markup: value + name + data-rask-on-change, `checked` present only
// on the currently selected option (mirrors Input.WriteAttributes / BsRadioGroup).
const radio = (value: string, checked: boolean) =>
    makeInput({"data-rask-on-change": "hR", "name": "plan", "value": value, ...(checked ? {"checked": ""} : {})},
        checked);

// ---- Scenario 1: diff codec (syncFormProperty) — a stale checked op is suppressed ----
// User clicks "Pro"; the browser sets pro.checked=true (attribute still absent).
// The dispatch notes the pre-click group state (from the `checked` attribute).
const proLive = radio("Pro", false);
proLive.checked = true;                              // native click flipped the property
raskNotePendingChecked(asDom(proLive), proLive.hasAttribute("checked")); // superseded = false
// A lagging RemoveAttribute-checked op (server's pre-click view) must NOT unset it.
syncFormProperty(asDom(proLive), "checked", "", false);
const s1AfterStale = proLive.checked;                // expect true — suppressed
// The authoritative echo (SetAttribute checked) applies and releases the guard.
syncFormProperty(asDom(proLive), "checked", "", true);
const s1AfterEcho = proLive.checked;                 // expect true — applied

// ---- Scenario 2: full morph, radio group — group guard blocks the whole revert ----
// Initial DOM: Free selected. User clicks Pro → pro.checked=true, free.checked=false
// natively; both `checked` attributes still reflect the server (free has it, pro doesn't).
const freeFrom = radio("Free", false); // free: attr present below; property now false
freeFrom.setAttribute("checked", "");  // server-rendered default still marks Free
freeFrom.checked = false;              // native exclusivity unchecked it
const proFrom = radio("Pro", false);
proFrom.checked = true;                // native click checked it (no attribute)
// Dispatch notes the whole group's pre-click state from the attributes.
raskNotePendingChecked(asDom(freeFrom), freeFrom.hasAttribute("checked")); // superseded = true
raskNotePendingChecked(asDom(proFrom), proFrom.hasAttribute("checked"));   // superseded = false
// A stale morph frame (server computed with Free still selected) must neither
// re-check Free nor uncheck Pro.
morph(asDom(freeFrom), asDom(radio("Free", true)));  // to: Free checked  → suppressed (== superseded true)
morph(asDom(proFrom), asDom(radio("Pro", false)));   // to: Pro unchecked → suppressed (== superseded false)
const s2FreeAfterStale = freeFrom.checked; // expect false
const s2ProAfterStale = proFrom.checked;   // expect true
// The echo (Pro selected) applies and releases both guards.
morph(asDom(freeFrom), asDom(radio("Free", false))); // to: Free unchecked → applies (!= superseded)
morph(asDom(proFrom), asDom(radio("Pro", true)));    // to: Pro checked    → applies (!= superseded)
const s2FreeAfterEcho = freeFrom.checked; // expect false
const s2ProAfterEcho = proFrom.checked;   // expect true
// After release, a later server-driven change wins (guard didn't pin the value).
morph(asDom(proFrom), asDom(radio("Pro", false)));   // programmatic deselect
const s2ProAfterLater = proFrom.checked;  // expect false

// ---- Scenario 3: full morph, lone checkbox — stale revert suppressed, echo applies ----
const cbFrom = makeInput({"data-rask-on-change": "hC"}, false);
cbFrom.checked = true;                                // user checked it (no attribute yet)
raskNotePendingChecked(asDom(cbFrom), cbFrom.hasAttribute("checked")); // superseded = false
morph(asDom(cbFrom), asDom(makeInput({"data-rask-on-change": "hC"}, false))); // stale: unchecked → suppressed
const s3AfterStale = cbFrom.checked;                  // expect true
morph(asDom(cbFrom), asDom(makeInput({"data-rask-on-change": "hC", "checked": ""}, true))); // echo → applies
const s3AfterEcho = cbFrom.checked;                   // expect true

process.stdout.write(JSON.stringify({
    s1AfterStale,       // true  — stale diff op suppressed
    s1AfterEcho,        // true  — echo applied
    s2FreeAfterStale,   // false — stale frame didn't re-check the old radio
    s2ProAfterStale,    // true  — stale frame didn't revert the clicked radio
    s2FreeAfterEcho,    // false — echo applied
    s2ProAfterEcho,     // true  — echo applied, guard released
    s2ProAfterLater,    // false — later server change wins (not pinned)
    s3AfterStale,       // true  — checkbox stale revert suppressed
    s3AfterEcho         // true  — checkbox echo applied
}) + "\n");
