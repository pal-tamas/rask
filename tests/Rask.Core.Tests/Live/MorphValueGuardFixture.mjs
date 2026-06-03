// Node-driven fixture for the shared client morph's user-edit echo guard
// (Rask.Core/Resources/rask-morph.js — raskNotePendingValue /
// raskShouldSuppressValue, consumed by both rask.js and rask.wasm.js).
//
// Reproduces the date-input desync: a user commits a value to a change-only
// input (date / number / select), and a re-render the server computed BEFORE
// that change reached it lands afterwards. Pre-fix, morph() unconditionally set
// the canonical (now-stale) server value back onto the input — the focus guard
// only protects the focused element, but a change commits on blur. The guard
// suppresses the stale value until the server echoes the user's value back, then
// lets server-canonical values win again.
//
// The C# test (MorphValueGuardTests) runs this in a node subprocess and asserts
// the single JSON line on stdout. Exits non-zero on an internal stub failure.
import { readFileSync } from "node:fs";

const morphPath = process.argv[2];
if (!morphPath) {
    console.error("usage: node MorphValueGuardFixture.mjs <rask-morph.js path>");
    process.exit(2);
}

// ----- Minimal element stub -----
// Models a real input AFTER user interaction: the `value` attribute (default) and
// the live `.value` property are independent, exactly as the browser keeps them
// once the dirty-value flag is set.
function makeInput(attrs) {
    const a = new Map(Object.entries(attrs || {}));
    const el = {
        nodeType: 1,
        nodeName: "INPUT",
        tagName: "INPUT",
        parentNode: null,
        firstChild: null,
        nextSibling: null,
        textContent: "",
        value: a.has("value") ? a.get("value") : "",
        checked: false,
        hasAttribute: (n) => a.has(n),
        getAttribute: (n) => (a.has(n) ? a.get(n) : null),
        setAttribute: (n, v) => a.set(n, String(v)),
        removeAttribute: (n) => a.delete(n),
        get attributes() {
            return [...a.entries()].map(([name, value]) => ({ name, value }));
        }
    };
    return el;
}

// A spectator element that owns focus, so morph's focus guard (activeElement !==
// from) lets the value-sync path run — the realistic post-blur state.
const elsewhere = { nodeType: 1, nodeName: "BODY", tagName: "BODY" };

globalThis.window = globalThis;
globalThis.document = {
    activeElement: elsewhere,
    createElement: () => makeInput({})
};

// ----- Load the shared morph snippet (plain function declarations, not a module) -----
const src = readFileSync(morphPath, "utf8");
const factory = new Function(
    src + "\n;return { morph, raskNotePendingValue, raskShouldSuppressValue };");
const { morph, raskNotePendingValue, raskShouldSuppressValue } = factory();

// Change-only inputs: data-rask-on-change present, NO data-rask-on-input, so morph
// treats the server value as canonical (the pre-fix clobber path). The runtime
// records the PRE-EDIT `value` attribute on the change dispatch — mirror that here.
const COMMITTED = "2019-12-31";
const DEFAULT = "2026-07-05";
const LATER = "2030-01-01";

// ---- Scenario 1: lagging stale render must not clobber a committed edit ----
const dateInput = makeInput({ "data-rask-on-change": "h78", "value": DEFAULT });
dateInput.value = DEFAULT;

// User edits to COMMITTED. Dispatch records the pre-edit attribute (DEFAULT).
raskNotePendingValue(dateInput, dateInput.getAttribute("value"));
dateInput.value = COMMITTED;

// A render the server computed BEFORE the change lands, carrying the stale DEFAULT.
morph(dateInput, makeInput({ "data-rask-on-change": "h78", "value": DEFAULT }));
const afterStale = dateInput.value;

// The authoritative echo of the user's value lands — applies, releases the guard.
morph(dateInput, makeInput({ "data-rask-on-change": "h78", "value": COMMITTED }));
const afterEcho = dateInput.value;

// A genuine later server-driven change wins (guard already released).
morph(dateInput, makeInput({ "data-rask-on-change": "h78", "value": LATER }));
const afterLater = dateInput.value;

// ---- Scenario 2: server CORRECTION must apply (the int-clear regression) ----
// User clears a non-nullable int (value="") whose model snaps to 0. The server's
// authoritative response is "0" — different from the user's "" AND from the pre-edit
// "30" — so it must win, not be suppressed.
const intInput = makeInput({ "data-rask-on-change": "h99", "value": "30" });
intInput.value = "30";
raskNotePendingValue(intInput, intInput.getAttribute("value")); // records "30"
intInput.value = "";                                            // user cleared
morph(intInput, makeInput({ "data-rask-on-change": "h99", "value": "0" }));
const afterCorrection = intInput.value;

process.stdout.write(JSON.stringify({
    afterStale,       // COMMITTED — stale render suppressed
    afterEcho,        // COMMITTED — echo applied, guard released
    afterLater,       // LATER     — later server value wins
    afterCorrection   // "0"       — server correction applied (not suppressed)
}) + "\n");
