// Node-driven fixture for the page's side of the Errors tab (host/errors.ts): the "Open in DevTools" button added to the
// runtime's dev-error overlay, and a show request held until the panel is listening.
//
// Stub DOM with exactly what the module touches. The C# test (HostErrorsTests) asserts the single JSON line on stdout.

import {
    addOverlayButton, installErrorsLink, OVERLAY_BUTTON_ATTRIBUTE, PAGE_ERROR_LIMIT, placeOf,
} from "../../../src/Rask.DevTools/Resources/host/errors.js";
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
const windowListeners: {type: string; handler: Handler}[] = [];
globals.window = {addEventListener: (type: string, handler: Handler) => windowListeners.push({type, handler})};
const fire = (type: string, e: unknown) => windowListeners.filter(l => l.type === type).forEach(l => l.handler(e));
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
const alerts: number[] = [];
const dock = {
    isOpen: () => open,
    toggle: () => { open = !open; toggles++; },
    setAlert: (n: number) => alerts.push(n),
} as unknown as DockHandle;
const posts: FrameMessage[] = [];
const link = installErrorsLink(dock, m => posts.push(m));

const later = overlay();
html.appendChild(later);
observed!();
out.decoratedWhenAdded = later.querySelector("[" + OVERLAY_BUTTON_ATTRIBUTE + "]") !== null;

// Page failures before the panel is listening: counted on the pill at once, held for the panel.
fire("error", {error: Object.assign(new TypeError("x is undefined"), {stack: "TypeError: x is undefined\n    at app.js:3"}),
    message: "Uncaught TypeError: x is undefined", filename: "https://app/app.js", lineno: 3, colno: 7});
fire("unhandledrejection", {reason: "nope"});
fire("error", {error: null, message: "Script error.", filename: "https://cdn.example/x.js", lineno: 1, colno: 2});
link.island("update", new El("rask-external") as unknown as Element, "Chart", new RangeError("bad range"));
out.alertsBeforePanel = alerts.join(",");

link.show();
out.openedByShow = open && toggles === 1;
out.postsBeforePanel = posts.length;
link.heardFromPanel();
link.heardFromPanel();
out.postsAfterPanel = posts.map(m => m.kind).join(",");
const reports = posts.flatMap(m => m.kind === "page-error" ? [m.report] : []);
out.reports = reports.map(r => `${r.kind}|${r.title}|${r.message}|${r.stack}|${r.island}|${r.phase}|${r.place}`);
out.reportTimesAreSet = reports.every(r => r.at > 0);
out.alertAfterHandOver = alerts[alerts.length - 1];
link.setPanelCount(3);
out.alertWithPanelCount = alerts[alerts.length - 1];
posts.length = 0;
fire("unhandledrejection", {reason: new Error("later")});
out.postedOnceListening = posts.map(m => m.kind === "page-error" ? m.report.message : m.kind).join(",");
out.alertUnchangedWhileListening = alerts[alerts.length - 1];
link.show(); // already open, panel already listening: straight through, without closing the drawer
out.postsAtOnce = posts.map(m => m.kind).join(",");
out.stillOpen = open && toggles === 1;

// The buffer: only the newest PAGE_ERROR_LIMIT wait for a panel that has not opened.
const bufferPosts: FrameMessage[] = [];
const bufferAlerts: number[] = [];
const quiet = installErrorsLink(
    {isOpen: () => false, toggle() {}, setAlert: (n: number) => bufferAlerts.push(n)} as unknown as DockHandle,
    m => bufferPosts.push(m));
for (let i = 0; i < PAGE_ERROR_LIMIT + 5; i++) {
    quiet.record({kind: "page", title: "Error", message: "e" + i, stack: null, at: 1, island: null, phase: null, place: null});
}
out.bufferAlert = bufferAlerts[bufferAlerts.length - 1];
quiet.heardFromPanel();
const handed = bufferPosts.flatMap(m => m.kind === "page-error" ? [m.report.message] : []);
out.bufferHanded = `${handed.length}:${handed[0]}:${handed[handed.length - 1]}`;

// A place for an island's element, counted the way the diff counts slots: managed nodes and whitespace under <html> skipped.
class Node2 {
    parentNode: Node2 | null = null;
    readonly childNodes: Node2[] = [];
    constructor(readonly nodeType: number, readonly nodeName: string, readonly nodeValue: string | null = null,
                readonly managed = false) {}
    hasAttribute(name: string) { return name === "data-rask-managed" && this.managed; }
    add(child: Node2) { child.parentNode = this; this.childNodes.push(child); return child; }
}
const doc2 = new Node2(9, "#document");
globals.document = doc2;
const html2 = doc2.add(new Node2(1, "HTML"));
html2.add(new Node2(1, "HEAD"));
html2.add(new Node2(3, "#text", "\n  "));
const body2 = html2.add(new Node2(1, "BODY"));
body2.add(new Node2(1, "SCRIPT", null, true));
body2.add(new Node2(1, "MAIN"));
const main2 = body2.childNodes[1];
main2.add(new Node2(3, "#text", "hello"));
const host2 = main2.add(new Node2(1, "RASK-EXTERNAL"));
out.islandPlace = placeOf(host2 as unknown as Element);
out.detachedPlace = placeOf(new Node2(1, "DIV") as unknown as Element);

process.stdout.write(JSON.stringify(out) + "\n");
