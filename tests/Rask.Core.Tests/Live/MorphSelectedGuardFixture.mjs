// Node-driven fixture for the shared client's `selected` echo guard
// (raskNotePendingSelected / raskShouldSuppressSelected in rask-morph.js, consumed by the diff codec's
// syncFormProperty in rask-dom.js, which in turn feeds rask.js and rask.wasm.js).
//
// Reproduces the <select> desync: the user picks an option (the browser flips that option's `selected`
// PROPERTY and leaves every `selected` ATTRIBUTE where the server put it), and a re-render the server
// computed BEFORE the pick reached it lands afterwards. Pre-fix the diff codec set `.selected`
// unconditionally — the third form property, and the only one with no guard — so the box snapped back to
// the old option until the echo arrived. The focus guard doesn't help: a select commits on change, so
// focus has moved on by then, exactly as with the date/number inputs the value guard was written for.
//
// Also covers the group-atomicity part: applying a selection through the SELECT (by index) rather than
// through the option's own property, so a diff that moves the selection can't leave a single-select
// briefly showing its first option.
//
// The C# test (MorphSelectedGuardTests) runs this in a node subprocess and asserts the single JSON line
// on stdout. Exits non-zero on an internal stub failure.
import {readFileSync} from "node:fs";

const morphPath = process.argv[2];
const domPath = process.argv[3];
if (!morphPath || !domPath) {
    console.error("usage: node MorphSelectedGuardFixture.mjs <rask-morph.js path> <rask-dom.js path>");
    process.exit(2);
}

// ----- Minimal <option> / <select> stubs -----
// Models the real state AFTER a native pick: the `selected` attribute (the last server-rendered default)
// and the live `.selected` property are independent — picking flips the property on two options and
// never touches either attribute.
function makeOption(value, selectedAttr) {
    const a = new Map([["value", value]]);
    if (selectedAttr) a.set("selected", "");
    const el = {
        nodeType: 1, nodeName: "OPTION", tagName: "OPTION",
        value, selected: !!selectedAttr, index: 0, parentNode: null,
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => a.set(n, String(v)),
        removeAttribute: (n) => a.delete(n),
        // The only selector applySelected asks for.
        closest: (sel) => (sel === "select" ? el.parentNode : null),
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); }
    };
    return el;
}

function makeSelect(options, multiple) {
    const a = new Map([["data-rask-on-change", "hS"]]);
    const sel = {
        nodeType: 1, nodeName: "SELECT", tagName: "SELECT", multiple: !!multiple, options,
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => a.set(n, String(v)),
        removeAttribute: (n) => a.delete(n),
        get attributes() { return [...a.entries()].map(([name, value]) => ({name, value})); },
        // Mirrors the browser: setting the index selects exactly one option and clears the rest, which is
        // the whole reason applySelected prefers it over poking each option.
        get selectedIndex() { return options.findIndex((o) => o.selected); },
        set selectedIndex(i) { options.forEach((o, j) => { o.selected = j === i; }); }
    };
    options.forEach((o, i) => { o.parentNode = sel; o.index = i; });
    return sel;
}

const elsewhere = {nodeType: 1, nodeName: "BODY", tagName: "BODY"};
globalThis.window = globalThis;
globalThis.document = {activeElement: elsewhere, createElement: () => makeOption("", false)};

// ----- Load the shared snippets (plain function declarations, not modules) -----
// Concat both: rask-dom.js's syncFormProperty calls raskShouldSuppressSelected, which lives in
// rask-morph.js — the runtime splices them into one scope.
const src = readFileSync(morphPath, "utf8") + "\n" + readFileSync(domPath, "utf8");
const factory = new Function(
    src + "\n;return { syncFormProperty, raskNotePendingSelected, raskNotePendingFormState };");
const {syncFormProperty, raskNotePendingFormState} = factory();

// ---- Scenario 1: a lagging frame must not undo the user's pick ----
// Server rendered "a" selected. The user picks "b"; the browser moves the property, both attributes
// still say what the server last sent. The dispatch notes the pre-pick attribute state of every option.
const s1a = makeOption("a", true);
const s1b = makeOption("b", false);
const s1 = makeSelect([s1a, s1b]);
s1a.selected = false;
s1b.selected = true;
raskNotePendingFormState(s1);

// The lagging frame (computed with "a" still chosen) re-marks a and un-marks b. Both are suppressed.
syncFormProperty(s1a, "selected", "", true);
syncFormProperty(s1b, "selected", "", false);
const s1AfterStaleA = s1a.selected;   // expect false — the old option was not re-selected
const s1AfterStaleB = s1b.selected;   // expect true  — the user's pick survived

// The authoritative echo ("b" chosen) applies and releases both guards.
syncFormProperty(s1a, "selected", "", false);
syncFormProperty(s1b, "selected", "", true);
const s1AfterEchoA = s1a.selected;    // expect false
const s1AfterEchoB = s1b.selected;    // expect true

// Released: a later server-driven change back to "a" is not pinned by the guard.
syncFormProperty(s1a, "selected", "", true);
const s1AfterLaterA = s1a.selected;   // expect true
const s1AfterLaterB = s1b.selected;   // expect false — moving the select cleared the other option

// ---- Scenario 2: a select nobody has touched still follows the server ----
// No dispatch, so no guard: the ordinary server-driven selection must apply unchanged.
const s2a = makeOption("a", true);
const s2b = makeOption("b", false);
makeSelect([s2a, s2b]);
syncFormProperty(s2a, "selected", "", false);
syncFormProperty(s2b, "selected", "", true);
const s2AfterServerA = s2a.selected;  // expect false
const s2AfterServerB = s2b.selected;  // expect true

// ---- Scenario 3: selecting moves the whole group in one write ----
// Applying through the SELECT's index (not the option's property) means the option that was on is off
// again without needing its own op — a diff whose remove-op is dropped can't leave two options selected.
const s3a = makeOption("a", true);
const s3b = makeOption("b", false);
const s3c = makeOption("c", false);
makeSelect([s3a, s3b, s3c]);
syncFormProperty(s3c, "selected", "", true);
const s3OnlyCSelected = s3c.selected && !s3a.selected && !s3b.selected;   // expect true

// ---- Scenario 4: a multi-select keeps per-option control ----
// Several options are legitimately on at once, so the atomic single-select path must not apply.
const s4a = makeOption("a", true);
const s4b = makeOption("b", false);
makeSelect([s4a, s4b], true);
syncFormProperty(s4b, "selected", "", true);
const s4BothSelected = s4a.selected && s4b.selected;                      // expect true

process.stdout.write(JSON.stringify({
    s1AfterStaleA,      // false — stale frame didn't re-select the old option
    s1AfterStaleB,      // true  — stale frame didn't revert the user's pick
    s1AfterEchoA,       // false — echo applied
    s1AfterEchoB,       // true  — echo applied
    s1AfterLaterA,      // true  — guard released, later server change wins
    s1AfterLaterB,      // false
    s2AfterServerA,     // false — an untouched select still follows the server
    s2AfterServerB,     // true
    s3OnlyCSelected,    // true  — one write moved the whole group
    s4BothSelected      // true  — multi-select keeps per-option control
}) + "\n");
