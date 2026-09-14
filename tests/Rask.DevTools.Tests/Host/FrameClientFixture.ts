// Node-driven fixture for the WASM panel's frame client (Rask.DevTools/Resources/rask-devtools-frame.ts): the panel's
// clicks and changes reach its session. They did not: the shared rask-events module forwards keys and pointers, and
// click and change forwarding lives in each host runtime, which the frame does not load — so every tab, button, toggle
// and tree row in a WASM panel did nothing, and no test looked.
//
// The C# test (FrameClientTests) asserts the single JSON line on stdout.

import {documentListeners, posted, StubElement} from "./FrameClientStubs.js";
import "../../../src/Rask.DevTools/Resources/rask-devtools-frame.js";

const out: Record<string, unknown> = {};
const events = () => posted.filter(m => (m as {kind?: string}).kind === "event").map(m => (m as {payload: unknown}).payload);

out.readyPosted = posted.some(m => (m as {kind?: string}).kind === "ready");

const fire = (type: string, e: Record<string, unknown>) =>
    documentListeners.filter(l => l.type === type).forEach(l => l.handler(e));

// A tab: a button with a click handler.
const tab = new StubElement("BUTTON");
tab.setAttribute("data-rask-on-click", "h1");
let prevented = false;
fire("click", {target: tab, shiftKey: false, ctrlKey: true, altKey: false, metaKey: false, preventDefault: () => prevented = true});
out.click = JSON.stringify(events().find(p => (p as {type?: string}).type === "click") ?? null);
out.clickPrevented = prevented;

// A click on nothing with a handler posts nothing.
const before = events().length;
fire("click", {target: new StubElement("DIV"), preventDefault: () => {}});
out.plainClickIgnored = events().length === before;

// A toggle: a checkbox with a change handler.
const toggle = new StubElement("INPUT");
toggle.type = "checkbox";
toggle.checked = true;
toggle.setAttribute("data-rask-on-change", "h2");
fire("change", {target: toggle});
out.change = JSON.stringify(events().find(p => (p as {type?: string}).type === "change") ?? null);

process.stdout.write(JSON.stringify(out) + "\n");
