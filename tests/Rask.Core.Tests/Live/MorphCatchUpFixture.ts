// Node-driven fixture for the whole page a connection is sent when it replaces one (MorphCatchUpTests).
//
// The fields stand where the reader left them: typed into or ticked, never sent, focus elsewhere. Each is morphed
// against a render that repeats what was rendered for it before, and against one that changed it — once as a
// catch-up, once as any other whole page.

import {morph} from "../../../src/Rask.Core/Resources/rask-morph.js";
import {asDom, installStubGlobals, type StubControl} from "./stub-dom.js";

function makeInput(attrs: Record<string, string>): StubControl {
    const a = new Map(Object.entries(attrs));
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
    return el as unknown as StubControl;
}

installStubGlobals({
    activeElement: {nodeType: 1, nodeName: "BUTTON", tagName: "BUTTON"},
    createElement: () => makeInput({})
});

function typed(catchUp: boolean, rendered: string): string {
    const note = makeInput({id: "note", value: ""});
    note.value = "half a thought";
    morph(asDom(note), asDom(makeInput({id: "note", value: rendered})), catchUp);
    return note.value;
}

// Rendered ticked, unticked by the reader.
function ticked(catchUp: boolean, rendered: boolean): boolean {
    const box = makeInput({id: "box", type: "checkbox", checked: ""});
    box.checked = false;
    morph(asDom(box), asDom(makeInput(rendered ? {id: "box", type: "checkbox", checked: ""} : {id: "box", type: "checkbox"})), catchUp);
    return box.checked;
}

process.stdout.write(JSON.stringify({
    catchUpRepeats: typed(true, ""),
    catchUpChanges: typed(true, "from the server"),
    pageRepeats: typed(false, ""),
    catchUpRepeatsTick: ticked(true, true),
    pageRepeatsTick: ticked(false, true)
}) + "\n");
