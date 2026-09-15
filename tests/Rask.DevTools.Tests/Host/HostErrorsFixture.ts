// Node-driven fixture for the page's side of the Errors tab (host/errors.ts): the "Open in DevTools" button added to the
// runtime's dev-error overlay, and a show request held until the panel is listening.
//
// Stub DOM with exactly what the module touches. The C# test (HostErrorsTests) asserts the single JSON line on stdout.

import {addOverlayButton, installErrorsLink, OVERLAY_BUTTON_ATTRIBUTE} from "../../../src/Rask.DevTools/Resources/host/errors.js";
import type {DockHandle} from "../../../src/Rask.DevTools/Resources/host/dock.js";
import type {FrameMessage} from "../../../src/Rask.DevTools/Resources/rask-devtools-frame-protocol.js";

type Handler = (e: unknown) => void;
const globals = globalThis as unknown as Record<string, unknown>;
const out: Record<string, unknown> = {};

class El {
    readonly children: El[] = [];
    readonly attributes = new Map<string, string>();
    readonly listeners: {type: string; handler: Handler}[] = [];
    className = "";
    textContent = "";
    type = "";
    constructor(readonly tag: string) {}
    setAttribute(name: string, value: string) { this.attributes.set(name, value); }
    appendChild(child: El) { this.children.push(child); return child; }
    insertBefore(child: El, before: El) { this.children.splice(this.children.indexOf(before), 0, child); return child; }
    addEventListener(type: string, handler: Handler) { this.listeners.push({type, handler}); }
    click() { this.listeners.filter(l => l.type === "click").forEach(l => l.handler({})); }
    all(): El[] { return this.children.flatMap(c => [c, ...c.all()]); }
    querySelector(selector: string): El | null {
        const match = (e: El) => selector.startsWith(".") ? e.className.split(" ").includes(selector.slice(1))
            : selector.startsWith("[") ? e.attributes.has(selector.slice(1, -1)) : e.tag === selector;
        return this.all().find(match) ?? null;
    }
}

// An overlay shaped like the runtime's: a bar with its own Stack and Dismiss buttons.
function overlay(): El {
    const root = new El("div");
    root.setAttribute("data-rask-dev-error", "");
    const bar = root.appendChild(new El("div"));
    bar.className = "rask-deverr__bar";
    bar.appendChild(new El("span")).className = "rask-deverr__kind";
    for (const text of ["Stack", "Dismiss"]) {
        const b = bar.appendChild(new El("button"));
        b.className = "rask-deverr__btn";
        b.textContent = text;
    }
    return root;
}

let observed: (() => void) | null = null;
const html = new El("html");
globals.document = {
    documentElement: html,
    createElement: (tag: string) => new El(tag),
    querySelector: (selector: string) => html.querySelector(selector),
};
globals.MutationObserver = class {
    constructor(cb: () => void) { observed = cb; }
    observe() {}
};

// ---- the button, added once, first in the bar, styled as the overlay's own ----------------------------------------------

let opened = 0;
const first = overlay();
out.added = addOverlayButton(first as unknown as Element, () => opened++);
out.addedAgain = addOverlayButton(first as unknown as Element, () => opened++);
const bar = first.querySelector(".rask-deverr__bar")!;
out.barOrder = bar.children.map(c => c.textContent || c.className).join(",");
const button = first.querySelector("[" + OVERLAY_BUTTON_ATTRIBUTE + "]")!;
out.buttonClass = button.className;
button.click();
out.opened = opened;
out.noBar = addOverlayButton(new El("div") as unknown as Element, () => {});

// ---- the link: the overlay found when the runtime adds it, a show request waiting for the panel --------------------------

let open = false;
let toggles = 0;
const dock = {isOpen: () => open, toggle: () => { open = !open; toggles++; }} as unknown as DockHandle;
const posts: FrameMessage[] = [];
const link = installErrorsLink(dock, m => posts.push(m));

const later = overlay();
html.appendChild(later);
observed!();
out.decoratedWhenAdded = later.querySelector("[" + OVERLAY_BUTTON_ATTRIBUTE + "]") !== null;

link.show();
out.openedByShow = open && toggles === 1;
out.postsBeforePanel = posts.length;
link.heardFromPanel();
link.heardFromPanel();
out.postsAfterPanel = posts.map(m => m.kind).join(",");
link.show(); // already open, panel already listening: straight through, without closing the drawer
out.postsAtOnce = posts.map(m => m.kind).join(",");
out.stillOpen = open && toggles === 1;

process.stdout.write(JSON.stringify(out) + "\n");
