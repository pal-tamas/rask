// Node-driven fixture for the shared client morph's user-edit echo guard
// (Rask.Core/Resources/rask-morph.js — raskNotePendingValue /
// consumed by both rask.js and rask.wasm.js).
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


// ----- Minimal element stub -----
// Models a real input AFTER user interaction: the `value` attribute (default) and
// the live `.value` property are independent, exactly as the browser keeps them
// once the dirty-value flag is set.
//
// The functions under test arrive by IMPORT. They used to be read off disk and evaluated with
// `new Function(src + "return { … }")`, because the shared modules were bare declarations meant to be
// pasted into a host's scope — there was no other way to reach them, nothing checked that the names
// in that string still existed, and it stops working the moment a module has real `export`s.

import {morph, raskNotePendingValue} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, installStubGlobals, type StubControl} from "./stub-dom.js";

function makeInput(attrs: Record<string, string>): StubControl {
    const a = new Map(Object.entries(attrs || {}));
    const el = {
        nodeType: 1,
        nodeValue: null,
        nodeName: "INPUT",
        tagName: "INPUT",
        parentNode: null,
        previousSibling: null,
        nextSibling: null,
        firstChild: null,
        textContent: "",
        value: a.has("value") ? a.get("value") : "",
        checked: false,
        hasAttribute: (n: string) => a.has(n),
        getAttribute: (n: string) => a.get(n) ?? null,
        setAttribute: (n: string, v: string) => a.set(n, String(v)),
        removeAttribute: (n: string) => a.delete(n),
        get attributes() {
            return [...a.entries()].map(([name, value]) => ({name, value}));
        }
    };
    // One cast, where the fiction is created: a partial fake asserted to be the node it stands
    // in for. Everything the scenarios do with it is then checked against that shape, and the
    // crossing into framework code is marked separately by asDom().
    return el as unknown as StubControl;
}

// A spectator element that owns focus, so morph's focus guard (activeElement !==
// from) lets the value-sync path run — the realistic post-blur state.
const elsewhere = {nodeType: 1, nodeName: "BODY", tagName: "BODY"};

installStubGlobals({
    activeElement: elsewhere,
    createElement: () => makeInput({})
});


// Change-only inputs: data-rask-on-change present, NO data-rask-on-input, so morph
// treats the server value as canonical (the pre-fix clobber path). The runtime
// records the PRE-EDIT `value` attribute on the change dispatch — mirror that here.
const COMMITTED = "2019-12-31";
const DEFAULT = "2026-07-05";
const LATER = "2030-01-01";

// ---- Scenario 1: lagging stale render must not clobber a committed edit ----
const dateInput = makeInput({"data-rask-on-change": "h78", "value": DEFAULT});
dateInput.value = DEFAULT;

// User edits to COMMITTED. Dispatch records the pre-edit attribute (DEFAULT).
raskNotePendingValue(asDom(dateInput), dateInput.getAttribute("value") ?? "");
dateInput.value = COMMITTED;

// A render the server computed BEFORE the change lands, carrying the stale DEFAULT.
morph(asDom(dateInput), asDom(makeInput({"data-rask-on-change": "h78", "value": DEFAULT})));
const afterStale = dateInput.value;

// The authoritative echo of the user's value lands — applies, releases the guard.
morph(asDom(dateInput), asDom(makeInput({"data-rask-on-change": "h78", "value": COMMITTED})));
const afterEcho = dateInput.value;

// A genuine later server-driven change wins (guard already released).
morph(asDom(dateInput), asDom(makeInput({"data-rask-on-change": "h78", "value": LATER})));
const afterLater = dateInput.value;

// ---- Scenario 2: server CORRECTION must apply (the int-clear regression) ----
// User clears a non-nullable int (value="") whose model snaps to 0. The server's
// authoritative response is "0" — different from the user's "" AND from the pre-edit
// "30" — so it must win, not be suppressed.
const intInput = makeInput({"data-rask-on-change": "h99", "value": "30"});
intInput.value = "30";
raskNotePendingValue(asDom(intInput), intInput.getAttribute("value") ?? ""); // records "30"
intInput.value = "";                                            // user cleared
morph(asDom(intInput), asDom(makeInput({"data-rask-on-change": "h99", "value": "0"})));
const afterCorrection = intInput.value;

process.stdout.write(JSON.stringify({
    afterStale,       // COMMITTED — stale render suppressed
    afterEcho,        // COMMITTED — echo applied, guard released
    afterLater,       // LATER     — later server value wins
    afterCorrection   // "0"       — server correction applied (not suppressed)
}) + "\n");
