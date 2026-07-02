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
import {readFileSync} from "node:fs";

const morphPath = process.argv[2];
const domPath = process.argv[3];
if (!morphPath || !domPath) {
    console.error("usage: node MorphCheckedGuardFixture.mjs <rask-morph.js path> <rask-dom.js path>");
    process.exit(2);
}

// ----- Minimal element stub -----
// Models a real input AFTER a native click: the `checked` attribute (the last
// server-rendered default) and the live `.checked` property are independent —
// clicking flips the property but never the attribute.
function makeInput(attrs, checked) {
    const a = new Map(Object.entries(attrs || {}));
    return {
        nodeType: 1,
        nodeName: "INPUT",
        tagName: "INPUT",
        parentNode: null,
        firstChild: null,
        nextSibling: null,
        textContent: "",
        value: a.has("value") ? a.get("value") : "",
        checked: !!checked,
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => a.set(n, String(v)),
        removeAttribute: (n) => a.delete(n),
        get attributes() {
            return [...a.entries()].map(([name, value]) => ({name, value}));
        }
    };
}

// A spectator element owns focus so the morph value/checked path runs (the
// realistic post-click state — focus is on the just-clicked control's group,
// never mid-typing here).
const elsewhere = {nodeType: 1, nodeName: "BODY", tagName: "BODY"};
globalThis.window = globalThis;
globalThis.document = {
    activeElement: elsewhere,
    createElement: () => makeInput({})
};

// ----- Load the shared snippets (plain function declarations, not modules) -----
// Concat both: rask-dom.js's syncFormProperty calls raskShouldSuppressChecked,
// which lives in rask-morph.js — the runtime splices them into one scope.
const src = readFileSync(morphPath, "utf8") + "\n" + readFileSync(domPath, "utf8");
const factory = new Function(
    src + "\n;return { morph, syncFormProperty, raskNotePendingChecked, raskShouldSuppressChecked };");
const {morph, syncFormProperty, raskNotePendingChecked} = factory();

// Radio option markup: value + name + data-rask-on-change, `checked` present only
// on the currently selected option (mirrors Input.WriteAttributes / BsRadioGroup).
const radio = (value, checked) =>
    makeInput({"data-rask-on-change": "hR", "name": "plan", "value": value, ...(checked ? {"checked": ""} : {})},
        checked);

// ---- Scenario 1: diff codec (syncFormProperty) — a stale checked op is suppressed ----
// User clicks "Pro"; the browser sets pro.checked=true (attribute still absent).
// The dispatch notes the pre-click group state (from the `checked` attribute).
const proLive = radio("Pro", false);
proLive.checked = true;                              // native click flipped the property
raskNotePendingChecked(proLive, proLive.hasAttribute("checked")); // superseded = false
// A lagging RemoveAttribute-checked op (server's pre-click view) must NOT unset it.
syncFormProperty(proLive, "checked", "", false);
const s1AfterStale = proLive.checked;                // expect true — suppressed
// The authoritative echo (SetAttribute checked) applies and releases the guard.
syncFormProperty(proLive, "checked", "", true);
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
raskNotePendingChecked(freeFrom, freeFrom.hasAttribute("checked")); // superseded = true
raskNotePendingChecked(proFrom, proFrom.hasAttribute("checked"));   // superseded = false
// A stale morph frame (server computed with Free still selected) must neither
// re-check Free nor uncheck Pro.
morph(freeFrom, radio("Free", true));  // to: Free checked  → suppressed (== superseded true)
morph(proFrom, radio("Pro", false));   // to: Pro unchecked → suppressed (== superseded false)
const s2FreeAfterStale = freeFrom.checked; // expect false
const s2ProAfterStale = proFrom.checked;   // expect true
// The echo (Pro selected) applies and releases both guards.
morph(freeFrom, radio("Free", false)); // to: Free unchecked → applies (!= superseded)
morph(proFrom, radio("Pro", true));    // to: Pro checked    → applies (!= superseded)
const s2FreeAfterEcho = freeFrom.checked; // expect false
const s2ProAfterEcho = proFrom.checked;   // expect true
// After release, a later server-driven change wins (guard didn't pin the value).
morph(proFrom, radio("Pro", false));   // programmatic deselect
const s2ProAfterLater = proFrom.checked;  // expect false

// ---- Scenario 3: full morph, lone checkbox — stale revert suppressed, echo applies ----
const cbFrom = makeInput({"data-rask-on-change": "hC"}, false);
cbFrom.checked = true;                                // user checked it (no attribute yet)
raskNotePendingChecked(cbFrom, cbFrom.hasAttribute("checked")); // superseded = false
morph(cbFrom, makeInput({"data-rask-on-change": "hC"}, false)); // stale: unchecked → suppressed
const s3AfterStale = cbFrom.checked;                  // expect true
morph(cbFrom, makeInput({"data-rask-on-change": "hC", "checked": ""}, true)); // echo → applies
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
