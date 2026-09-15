// Node-driven fixture for the page overlays' logic: where a place is (host/anchors.ts), what the page does with the panel
// frame's messages (host/bridge.ts), how a box is measured from the page's nodes and how a node's path is read back
// (host/overlay.ts over Rask.Core's rask-dom-path.ts), and what the panel's side posts and reports (panel/panel-client.ts).
//
// Stub DOMs with exactly what each module touches. The C# test (HostOverlayTests) asserts the single JSON line on stdout.

import {contains, findAnchor, parseAnchors, parsePlace} from "../../../src/Rask.DevTools/Resources/host/anchors.js";
import {createBridge, installPatchTiming, listenToPanel} from "../../../src/Rask.DevTools/Resources/host/bridge.js";
import type {DockHandle} from "../../../src/Rask.DevTools/Resources/host/dock.js";
import {labelText, measure, type Overlay} from "../../../src/Rask.DevTools/Resources/host/overlay.js";
import {changedElements, type Flash} from "../../../src/Rask.DevTools/Resources/host/flash.js";
import {installPanelClient, parseFlashes} from "../../../src/Rask.DevTools/Resources/panel/panel-client.js";
import {nodePath} from "../../../src/Rask.Core/Resources/rask-dom-path.js";
import type {FrameMessage} from "../../../src/Rask.DevTools/Resources/rask-devtools-frame-protocol.js";

type Handler = (e: unknown) => void;
const globals = globalThis as unknown as Record<string, unknown>;
const out: Record<string, unknown> = {};

// ---- anchors ---------------------------------------------------------------------------------------------------------

out.placeParsed = JSON.stringify(parsePlace("1.0.2|3|2"));
out.topPlaceParsed = JSON.stringify(parsePlace("|0|1"));
out.malformedRejected = [null, undefined, "", "1|2", "a|0|1", "1|-1|1", "1|0|0", "1.x|0|1"]
    .every(at => parsePlace(at as string) === null);
out.anchorsKeptOnlyValid = parseAnchors('[["1","|0|1","App"],["bad"],["2","nope","X"],["3","0|1|2","Row"]]').length;
out.anchorsOfGarbage = parseAnchors("{not json").length;

const place = parsePlace("1.0|2|2")!;
out.containsFirst = contains(place, [1, 0, 2]);
out.containsInsideLast = contains(place, [1, 0, 3, 5]);
out.containsNotAfter = !contains(place, [1, 0, 4]);
out.containsNotParent = !contains(place, [1, 0]);
out.containsNotSibling = !contains(place, [1, 1, 2]);

const anchors = parseAnchors(JSON.stringify([
    ["card", "1|0|1", "Card"],
    ["row", "1.0|0|3", "Row"],
    ["inner", "1.0|1|1", "Inner"],
    ["wrapper-of-inner", "1.0|1|1", "InnerChild"],
]));
out.deepestWins = findAnchor(anchors, [1, 0, 2])?.id;
out.tieGoesToLater = findAnchor(anchors, [1, 0, 1, 0])?.id;
out.outsideIsNull = findAnchor(anchors, [0, 0]) === null;

// ---- bridge ----------------------------------------------------------------------------------------------------------

const calls: string[] = [];
let picking = false;
let onPicked: ((id: string) => void) | null = null;
const overlay: Overlay = {
    show: (at, label) => calls.push(`show ${at} ${label}`),
    hide: () => calls.push("hide"),
    pick: (list, picked) => {
        picking = true;
        onPicked = picked;
        calls.push(`pick ${list.map(a => a.id).join(",")}`);
    },
    stopPicking: () => {
        picking = false;
        calls.push("stop");
    },
    isPicking: () => picking,
};
let toggles = 0;
const alerts: number[] = [];
const dock = {toggle: () => toggles++, setAlert: (n: number) => alerts.push(n)} as unknown as DockHandle;
let panelHeard = 0;
const posted: FrameMessage[] = [];
const storage = new Map<string, string>();
globals.localStorage = {
    getItem: (k: string) => storage.get(k) ?? null,
    setItem: (k: string, v: string) => storage.set(k, v),
    removeItem: (k: string) => storage.delete(k),
};
const flashCalls: string[] = [];
const flash: Flash = {
    renders: boxes => flashCalls.push(`renders ${boxes.map(b => b.join("=")).join(";")}`),
    setEnabled: on => flashCalls.push(`enabled ${on}`),
    isEnabled: () => false,
};
const bridge = createBridge(dock, overlay, flash, m => posted.push(m), {show() {}, heardFromPanel: () => panelHeard++});
const ch = "rask-devtools" as const;

bridge.handle({channel: ch, kind: "highlight", at: "1|0|1", label: "Card"});
bridge.handle({channel: ch, kind: "highlight", at: null, label: null});
bridge.handle({channel: ch, kind: "pick", anchors: '[["7","1|0|1","Card"]]'});
onPicked!("7");
bridge.handle({channel: ch, kind: "toggle"});
out.errorCountHandled = bridge.handle({channel: ch, kind: "error-count", count: 4});
bridge.handle({channel: ch, kind: "error-count", count: "x" as unknown as number});
out.alerts = alerts.join(",");
bridge.handle({channel: ch, kind: "flash-setting", on: true});
out.flashRemembered = storage.get("rask.devtools.flash") ?? null;
bridge.handle({channel: ch, kind: "flash", boxes: [["1|0|1", "Row · state"], ["bad"], 7] as unknown as [string, string][]});
bridge.handle({channel: ch, kind: "flash-setting", on: false});
out.flashForgotten = !storage.has("rask.devtools.flash");
out.flashCalls = flashCalls.join(" | ");
out.unknownKindNotHandled = !bridge.handle({channel: ch, kind: "ready"});
bridge.closed();
out.closeWhilePickingCancelled = posted.some(m => m.kind === "pick-cancelled");
bridge.handle({channel: ch, kind: "pick", anchors: null});
const postedBeforeIdleClose = posted.length;
bridge.closed();
out.closeWhileIdlePostsNothing = posted.length === postedBeforeIdleClose;
out.bridgeCalls = calls.join(" | ");
out.pickedPosted = posted.some(m => m.kind === "picked" && m.id === "7");
out.toggles = toggles;

// listenToPanel: only the frame's window, only in the origin named, only our channel.
const windowListeners: {type: string; handler: Handler}[] = [];
globals.window = {
    addEventListener: (type: string, handler: Handler) => windowListeners.push({type, handler}),
};
const frameWindow = {} as Window;
const heard: string[] = [];
listenToPanel(() => frameWindow, "https://app.test", () => ({
    handle: (m: FrameMessage) => {
        heard.push(m.kind);
        return m.kind !== "event";
    },
    closed: () => {},
}), m => heard.push("other:" + m.kind));
const deliver = (source: unknown, origin: string, data: unknown) =>
    windowListeners.filter(l => l.type === "message").forEach(l => l.handler({source, origin, data}));
deliver({}, "https://app.test", {channel: ch, kind: "toggle"});
deliver(frameWindow, "https://evil.test", {channel: ch, kind: "toggle"});
deliver(frameWindow, "https://app.test", {channel: "someone-else", kind: "toggle"});
deliver(frameWindow, "https://app.test", "not an object");
deliver(frameWindow, "https://app.test", {channel: ch, kind: "highlight", at: null, label: null});
deliver(frameWindow, "https://app.test", {channel: ch, kind: "event", payload: 1});
out.heard = heard.join(",");

// ---- measure and nodePath --------------------------------------------------------------------------------------------

class Rect {
    constructor(public left: number, public top: number, public width: number, public height: number) {}
    get right() { return this.left + this.width; }
    get bottom() { return this.top + this.height; }
}
globals.DOMRect = Rect;

class StubNode {
    parentNode: StubNode | null = null;
    readonly childNodes: StubNode[] = [];
    readonly attributes = new Map<string, string>();
    rect: Rect | null = null;
    constructor(public nodeType: number, public nodeName: string, public nodeValue: string | null = null) {}
    append(...kids: StubNode[]) {
        for (const kid of kids) {
            kid.parentNode = this;
            this.childNodes.push(kid);
        }
        return this;
    }
    hasAttribute(name: string) { return this.attributes.has(name); }
    getBoundingClientRect() { return this.rect ?? new Rect(0, 0, 0, 0); }
}
const el = (name: string, rect?: Rect) => {
    const n = new StubNode(1, name.toUpperCase());
    if (rect) n.rect = rect;
    return n;
};

const doc = new StubNode(9, "#document");
const html = el("html");
const head = el("head");
const body = el("body");
const managed = el("style");
managed.attributes.set("data-rask-managed", "");
const a = el("div", new Rect(10, 20, 100, 30));
const comment = new StubNode(8, "#comment");
const b = el("section", new Rect(40, 60, 200, 10));
const c = el("aside", new Rect(0, 0, 0, 0));
doc.append(new StubNode(10, "html"), html);
html.append(head, new StubNode(3, "#text", "\n"), body);
body.append(managed, a, comment, b, c);
globals.document = doc;

// body is [html slot 1 of document] → [slot 1 of html, the formatting newline not counted].
const union = measure(parsePlace("1.1|0|2")!);
out.union = union ? [union.left, union.top, union.width, union.height].join(",") : null;
out.zeroSizeSkipped = measure(parsePlace("1.1|2|1")!) === null;
out.unresolvedIsNull = measure(parsePlace("1.9|0|1")!) === null;
out.pathOfSection = JSON.stringify(nodePath(b as unknown as Node));
out.pathOfManagedIsNull = nodePath(managed as unknown as Node) === null;
out.label = labelText("TaskRow", new Rect(0, 0, 612.4, 43.6) as unknown as DOMRect);

// ---- panel client ----------------------------------------------------------------------------------------------------

class StubEl {
    readonly attributes = new Map<string, string>();
    readonly dispatched: {key: string; bubbles: boolean}[] = [];
    constructor(public cls = "", public child: StubEl | null = null, public parent: StubEl | null = null) {}
    getAttribute(name: string) { return this.attributes.get(name) ?? null; }
    closest(selector: string) {
        for (let n: StubEl | null = this; n; n = n.parent) if (selector === "." + n.cls) return n;
        return null;
    }
    querySelector(_selector: string) { return this.child; }
    dispatchEvent(e: {key: string; bubbles: boolean}) { this.dispatched.push(e); return true; }
}
globals.Element = StubEl;
globals.KeyboardEvent = class {
    constructor(public type: string, init: {key: string; bubbles: boolean}) { Object.assign(this, init); }
};
let observerCallback: (() => void) | null = null;
globals.MutationObserver = class {
    constructor(cb: () => void) { observerCallback = cb; }
    observe() {}
};

const docListeners: {type: string; handler: Handler}[] = [];
const rootListeners: {type: string; handler: Handler}[] = [];
const panelWindowListeners: {type: string; handler: Handler}[] = [];
let anchorsEl: StubEl | null = null;
let flashEl: StubEl | null = null;
let errorsEl: StubEl | null = null;
let flashesEl: StubEl | null = null;
const pickedEl = new StubEl();
const patchEl = new StubEl();
globals.window = {
    addEventListener: (type: string, handler: Handler) => panelWindowListeners.push({type, handler}),
};
globals.document = {
    addEventListener: (type: string, handler: Handler) => docListeners.push({type, handler}),
    documentElement: {addEventListener: (type: string, handler: Handler) => rootListeners.push({type, handler})},
    querySelector: (selector: string) =>
        selector === "[data-rask-devtools-anchors]" ? anchorsEl
        : selector === "[data-rask-devtools-picked]" ? pickedEl
        : selector === "[data-rask-devtools-flash]" ? flashEl
        : selector === "[data-rask-devtools-flashes]" ? flashesEl
        : selector === "[data-rask-devtools-patch]" ? patchEl
        : selector === "[data-rask-devtools-errors]" ? errorsEl
        : null,
};

const panelPosts: FrameMessage[] = [];
const page = {};
installPanelClient({post: m => panelPosts.push(m), fromPage: e => e.source === page});

const place1 = new StubEl();
place1.attributes.set("data-rask-devtools-at", "1|0|1");
place1.attributes.set("data-rask-devtools-label", "Card");
const row = new StubEl("ui-tree-row", place1);
const inRow = new StubEl("", null, row);
const outside = new StubEl();
const over = (target: StubEl) => docListeners.filter(l => l.type === "pointerover").forEach(l => l.handler({target}));
over(inRow);
over(inRow); // same row: no second post
over(outside);
rootListeners.filter(l => l.type === "pointerleave").forEach(l => l.handler({}));
out.hoverPosts = panelPosts.map(m => m.kind === "highlight" ? `${m.at}:${m.label}` : m.kind).join(",");

panelPosts.length = 0;
anchorsEl = new StubEl();
anchorsEl.attributes.set("data-rask-devtools-anchors", "[]");
observerCallback!();
observerCallback!(); // unchanged: no second post
anchorsEl = null;
observerCallback!();
out.pickPosts = panelPosts.map(m => m.kind === "pick" ? `pick:${m.anchors}` : m.kind).join(",");

const message = (source: unknown, data: unknown) =>
    panelWindowListeners.filter(l => l.type === "message").forEach(l => l.handler({source, data}));
message({}, {channel: ch, kind: "picked", id: "42"});
message(page, {channel: ch, kind: "picked", id: "42"});
message(page, {channel: ch, kind: "pick-cancelled"});
out.pickedKeys = pickedEl.dispatched.map(e => `${e.key}/${e.bubbles}`).join(",");

// The Errors tab's count, handed to the page when it changes; the page asking to show the errors before the tab strip has
// rendered is held, then handed to it as a keydown once it has.
out.heardFromPanel = panelHeard > 0;
panelPosts.length = 0;
message(page, {channel: ch, kind: "show-errors"});
errorsEl = new StubEl();
errorsEl.attributes.set("data-rask-devtools-errors", "2");
observerCallback!();
observerCallback!(); // unchanged: no second post, and the request is handed over once
errorsEl.attributes.set("data-rask-devtools-errors", "0");
observerCallback!();
out.errorPosts = panelPosts.map(m => m.kind === "error-count" ? `count:${m.count}` : m.kind).join(",");
out.showErrorsKeys = errorsEl.dispatched.map(e => e.key).join(",");

// Patch times from the page, collected and reported together: one keydown for a burst, only from the page, and nothing
// that is not a finite non-negative number.
const timers: (() => void)[] = [];
globals.setTimeout = (fn: () => void) => { timers.push(fn); return timers.length; };
message({}, {channel: ch, kind: "patch", ms: 9, bytes: 1});
message(page, {channel: ch, kind: "patch", ms: 3.456, bytes: 2048});
message(page, {channel: ch, kind: "patch", ms: -1, bytes: 1});
message(page, {channel: ch, kind: "patch", ms: "7", bytes: 1});
message(page, {channel: ch, kind: "patch", ms: 4, bytes: 1.5});
message(page, {channel: ch, kind: "patch", ms: 12, bytes: -1});
out.patchTimersScheduled = timers.length;
timers.splice(0).forEach(fn => fn());
out.patchKeys = patchEl.dispatched.map(e => e.key).join(",");

// The page's side: the runtime's hook, installed, posts how long a frame took to apply.
const patchPosts: FrameMessage[] = [];
installPatchTiming(m => patchPosts.push(m));
const hook = (globals.window as {__raskDevtoolsHook?: RaskDevtoolsHook}).__raskDevtoolsHook!;
const seenFrame = {} as RaskFrameReply;
hook.send({}, 1);
hook.recv(seenFrame, 357);
hook.commit(seenFrame, performance.now() - 5);
hook.commit({} as RaskFrameReply, performance.now());
out.patchPosted = patchPosts.map(m => m.kind === "patch" ? `${m.ms >= 5 && m.ms < 1000 ? "timed" : m.ms >= 0 ? "zero" : "?"}@${m.bytes}` : m.kind)
    .join(",");

// Flashing, from the panel's side. The page remembered it on; the panel renders it off, so the setting is reported to the
// panel once as a keydown, and nothing reaches the page until the panel agrees.
panelPosts.length = 0;
storage.set("rask.devtools.flash", "on");
flashEl = new StubEl();
flashEl.attributes.set("data-rask-devtools-flash", "off");
observerCallback!();
out.flashReported = flashEl.dispatched.map(e => e.key).join(",");
out.flashPostsBeforeAgreement = panelPosts.length;
// The panel re-renders on, with commits 3 and 4 already held: the setting goes to the page, the held commits are the
// baseline and flash nothing.
flashEl.attributes.set("data-rask-devtools-flash", "on");
flashesEl = new StubEl();
flashesEl.attributes.set("data-rask-devtools-flashes", '[[3,[["1|0|1","A · mount"]]],[4,[["1|0|2","B · state"]]]]');
observerCallback!();
// Commit 5 arrives, then the same attribute again: flashed once. A commit with no places posts nothing.
flashesEl.attributes.set("data-rask-devtools-flashes",
    '[[4,[["1|0|2","B · state"]]],[5,[["1|0|3","C · props"]]],[6,[]]]');
observerCallback!();
observerCallback!();
// Switched off by the reader: the page is told, and no report is made again.
flashEl.attributes.set("data-rask-devtools-flash", "off");
flashesEl = null;
observerCallback!();
out.flashPosts = panelPosts.map(m =>
    m.kind === "flash-setting" ? `setting:${m.on}` : m.kind === "flash" ? `flash:${m.boxes.map(b => b[1]).join(";")}` : m.kind)
    .join(",");
out.flashReportedOnce = flashEl.dispatched.length === 1;
out.flashesOfGarbage = parseFlashes("{nope").length + parseFlashes('[["x",[]],[1,"no"]]').length;
out.flashesKeepValidBoxes = JSON.stringify(parseFlashes('[[2,[["a","b"],["c"],[1,2]]]]'));

// The DOM flash: what a batch of mutations changed, each element once, never the devtools' own element or anything in
// <head>, no box for the document or body toggling an attribute, and the container when only text was added.
class Node2 {
    constructor(public nodeType: number, public name: string, public parentElement: Node2 | null = null) {}
    closest(selector: string) {
        for (let n: Node2 | null = this; n; n = n.parentElement) if (selector === n.name) return n;
        return null;
    }
    contains(other: Node2) {
        for (let n: Node2 | null = other; n; n = n.parentElement) if (n === this) return true;
        return false;
    }
}
const htmlEl = new Node2(1, "html");
const headEl = new Node2(1, "head", htmlEl);
const bodyEl = new Node2(1, "body", htmlEl);
const list = new Node2(1, "ul", bodyEl);
const item = new Node2(1, "li", list);
const text = new Node2(3, "#text", item);
const hostEl = new Node2(1, "rask-devtools", htmlEl);
globals.document = {documentElement: htmlEl, body: bodyEl};
const record = (type: string, target: Node2, added: Node2[] = []) =>
    ({type, target, addedNodes: {length: added.length, forEach: (f: (n: Node2) => void) => added.forEach(f)}});
const changed = changedElements([
    record("characterData", text),
    record("attributes", item),
    record("childList", list, [item]),
    record("childList", list, [new Node2(3, "#text", list)]),
    record("attributes", bodyEl),
    record("attributes", htmlEl),
    record("childList", htmlEl, [hostEl]),
    record("attributes", hostEl),
    record("childList", headEl, [new Node2(1, "style", headEl)]),
] as unknown as MutationRecord[], hostEl as unknown as Element);
out.domChanged = (changed as unknown as Node2[]).map(n => n.name).join(",");

process.stdout.write(JSON.stringify(out) + "\n");
