// Node-driven fixture for the island diff boundary in the shared client morph
// (Rask.Core/Resources/rask-morph.js — the data-rask-opaque early return).
//
// The scenario is the one that silently corrupts. The server renders an island as an EMPTY element:
// its children are created in the browser by React/Lit/Blazor after mount. So on any full-document
// morph — scoped-CSS delivery, a reconnect, any untrusted structural op — the incoming side has no
// children for that element while the live DOM has whatever the foreign renderer built. A positional
// walk therefore trims every mounted node, and the island goes blank until something re-mounts it.
//
// Attributes must still sync across the boundary: that is how a changed `props` reaches the adapter.
//
// The C# test (OpaqueMorphTests) runs this in a node subprocess and asserts the JSON on stdout.
import {loadMorph, makeEl, withKids} from "./MorphDomStub.mjs";

const morphPath = process.argv[2];
if (!morphPath) {
    console.error("usage: node OpaqueMorphFixture.mjs <rask-morph.js path>");
    process.exit(2);
}

const {morph} = loadMorph(morphPath);

/** A live island host: server-rendered attributes, browser-created children. */
function liveIsland(props, opaque) {
    const attrs = {name: "Chart", props};
    if (opaque) attrs["data-rask-opaque"] = "";
    return withKids("RASK-ISLAND", [
        withKids("DIV", [makeEl("SVG"), makeEl("SPAN", {}, "41,200")], {class: "recharts-wrapper"})
    ], attrs);
}

/** What the server re-renders: the same host, new props, and NO children. */
function renderedIsland(props, opaque) {
    const attrs = {name: "Chart", props};
    if (opaque) attrs["data-rask-opaque"] = "";
    return makeEl("RASK-ISLAND", attrs);
}

function run(opaque) {
    const from = withKids("DIV", [liveIsland('{"total":1}', opaque)]);
    const to = withKids("DIV", [renderedIsland('{"total":2}', opaque)]);

    let threw = false;
    let error = "";
    try {
        morph(from, to);
    } catch (e) {
        threw = true;
        error = String((e && e.message) || e);
    }

    const island = from._kids[0];
    return {
        threw,
        error,
        // Survivors of the island's own subtree — the thing the boundary protects.
        survivingChildren: island ? island._kids.map((c) => c.nodeName) : [],
        // Props must cross the boundary even though children do not.
        props: island ? island.getAttribute("props") : null
    };
}

process.stdout.write(JSON.stringify({
    opaque: run(true),
    // Negative control: identical shapes with the marker off. If this one ALSO keeps its children the
    // fixture is proving nothing, because the morph never wanted to remove them in the first place.
    transparent: run(false)
}) + "\n");
