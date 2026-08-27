// Node-driven fixture for the shared client morph's keyed-children reconciliation
// (Rask.Core/Resources/rask-morph.js — the keyed branch under `if (keyed)`).
//
// Reproduces the WASM static-host hydration crash: the App renders a full <head>
// whose scoped-bundle <link> carries data-rask-key="rsk-css", which promotes the
// whole <head> to keyed reconciliation. The document it hydrates against is the
// SDK index.html <head> — <base> + an importmap <script> + <title>, none keyed.
// Those from-side nodes don't match the App's head by node name and get removed;
// pre-fix the keyed loop's `anchor` still pointed at a removed node, so the next
// insert threw "insertBefore ... reference node is not a child" and the runtime
// never finished its first morph (blank page). The fix advances the anchor past a
// node before removing it.
//
// The C# test (KeyedHeadMorphTests) runs this in a node subprocess and asserts the
// single JSON line on stdout. Pairs with the StandaloneWasm E2E (the host that
// exercises this exact hydration).
import {loadMorph, makeEl, withKids} from "./MorphDomStub.mjs";

const morphPath = process.argv[2];
if (!morphPath) {
    console.error("usage: node KeyedHeadMorphFixture.mjs <rask-morph.js path>");
    process.exit(2);
}

const {morph} = loadMorph(morphPath);

// from = the SDK index.html <head> a WASM static host serves (no data-rask-key).
const fromHead = withKids("HEAD", [
    makeEl("BASE", {href: "/"}),
    makeEl("SCRIPT", {type: "importmap"}, "{}"),
    makeEl("TITLE", {}, "Old")
]);

// to = the App's rendered <head>: a <title> plus the keyed scoped-bundle <link>.
const toHead = withKids("HEAD", [
    makeEl("TITLE", {}, "Rask"),
    makeEl("LINK", {rel: "stylesheet", href: "/_rask/a/abc123def456.css", "data-rask-key": "rsk-css"})
]);

let threw = false;
let error = "";
try {
    morph(fromHead, toHead);
} catch (e) {
    threw = true;
    error = String((e && e.message) || e);
}

process.stdout.write(JSON.stringify({
    threw,
    error,
    children: fromHead._kids.map((c) => c.nodeName)
}) + "\n");
