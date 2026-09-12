// Node-driven fixture for the devtools pill and drawer (Rask.DevTools/Resources/host/dock.ts).
//
// Drives the production installDock against a stub DOM that has what the dock uses and nothing more: elements with
// attributes, children, listeners and a shadow root; a window that records its listeners; a localStorage that can be
// made to throw. Scenarios, in order:
//   1. install: one <rask-devtools data-rask-managed> after <body>, a closed drawer, no panel frame yet;
//   2. the pill opens the drawer and creates the frame, once;
//   3. Ctrl+Shift+D closes it, is swallowed (preventDefault + stopImmediatePropagation) and hands focus back to the
//      pill; a plain Ctrl+D is left alone; Cmd+Shift+D reopens without a second frame;
//   4. docking right is remembered by the next install;
//   5. with storage blocked the dock still installs and docks, and nothing throws;
//   6. with no panel URL, opening creates no frame;
//   7. with a frame document instead (the WASM host), one srcdoc frame, and onFrame once, with the frame attached.
//
// The C# test (HostDockTests) runs this and asserts the single JSON line on stdout.

import {DOCK_STORAGE_KEY, installDock} from "../../../src/Rask.DevTools/Resources/host/dock.js";

type Handler = (e: unknown) => void;

let focused: StubElement | null = null;

class StubElement {
    readonly nodeType = 1;
    readonly tagName: string;
    readonly children: StubElement[] = [];
    readonly attributes = new Map<string, string>();
    readonly listeners: { type: string; handler: Handler; capture: boolean }[] = [];
    parentNode: StubElement | null = null;
    shadowRoot: StubElement | null = null;
    textContent = "";
    className = "";
    id = "";
    title = "";
    type = "";
    src = "";
    hidden = false;

    constructor(tagName: string) {
        this.tagName = tagName.toUpperCase();
    }

    setAttribute(name: string, value: string) { this.attributes.set(name, String(value)); }
    getAttribute(name: string) { return this.attributes.get(name) ?? null; }
    hasAttribute(name: string) { return this.attributes.has(name); }
    appendChild(child: StubElement) { child.parentNode = this; this.children.push(child); return child; }
    attachShadow(_init: { mode: string }) { this.shadowRoot = new StubElement("#shadow-root"); return this.shadowRoot; }
    addEventListener(type: string, handler: Handler, capture?: boolean) {
        this.listeners.push({type, handler, capture: !!capture});
    }
    focus() { focused = this; }
    click() { for (const l of this.listeners) if (l.type === "click") l.handler({}); }
    find(tagName: string): StubElement[] {
        const hits: StubElement[] = [];
        for (const c of this.children) {
            if (c.tagName === tagName.toUpperCase()) hits.push(c);
            hits.push(...c.find(tagName));
        }
        return hits;
    }
}

const storage = new Map<string, string>();
let storageBlocked = false;
const windowListeners: { type: string; handler: Handler; capture: boolean }[] = [];

const globals = globalThis as unknown as Record<string, unknown>;
globals.window = globalThis;
globals.addEventListener = (type: string, handler: Handler, capture?: boolean) =>
    windowListeners.push({type, handler, capture: !!capture});
Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    get() {
        if (storageBlocked) throw new Error("SecurityError: storage is blocked");
        return {
            getItem: (k: string) => storage.get(k) ?? null,
            setItem: (k: string, v: string) => { storage.set(k, v); },
        };
    },
});

function newDocument() {
    const html = new StubElement("html");
    const body = new StubElement("body");
    html.appendChild(new StubElement("head"));
    html.appendChild(body);
    globals.document = {createElement: (tag: string) => new StubElement(tag), documentElement: html, body};
    return {html, body};
}

function parts(element: StubElement) {
    const shadow = element.shadowRoot!;
    const [, pill, drawer] = shadow.children;
    const buttons = drawer.find("button");
    return {pill, drawer, toBottom: buttons[0], toRight: buttons[1], close: buttons[2]};
}

function key(init: { ctrlKey?: boolean; metaKey?: boolean; shiftKey?: boolean }) {
    const e = {code: "KeyD", altKey: false, ctrlKey: false, metaKey: false, shiftKey: false, ...init,
        prevented: false, stopped: false,
        preventDefault() { e.prevented = true; },
        stopImmediatePropagation() { e.stopped = true; }};
    for (const l of windowListeners) if (l.type === "keydown") l.handler(e);
    return e;
}

// 1. Install.
const first = newDocument();
const handle = installDock({panelUrl: "/_rask-devtools/?inspect=s1"});
const el = handle.element as unknown as StubElement;
const ui = parts(el);
const mountedAfterBody = first.html.children.indexOf(el) === first.html.children.indexOf(first.body) + 1;
const managed = el.hasAttribute("data-rask-managed");
const closedAtStart = ui.drawer.hidden && !handle.isOpen();
const framesAtStart = ui.drawer.find("iframe").length;
const sideAtStart = handle.side();
const shortcutListenerIsCapture = windowListeners.some(l => l.type === "keydown" && l.capture);

// 2. The pill opens.
ui.pill.click();
const openAfterPill = handle.isOpen() && ui.pill.getAttribute("aria-expanded") === "true";
const frames = ui.drawer.find("iframe");
const frameSrc = frames[0]?.src ?? null;

// 3. The shortcut.
const closing = key({ctrlKey: true, shiftKey: true});
const closedByShortcut = !handle.isOpen();
const shortcutSwallowed = closing.prevented && closing.stopped;
const focusReturnedToPill = focused === ui.pill;
const plain = key({ctrlKey: true});
const plainIgnored = !plain.prevented && !plain.stopped && !handle.isOpen();
key({metaKey: true, shiftKey: true});
const reopenedByMac = handle.isOpen();
const framesAfterReopen = ui.drawer.find("iframe").length;

// 4. Dock right, remembered.
ui.toRight.click();
const storedSide = storage.get(DOCK_STORAGE_KEY) ?? null;
const drawerSide = ui.drawer.getAttribute("data-side");
const rightPressed = ui.toRight.getAttribute("aria-pressed");
newDocument();
const sideOnNextInstall = installDock({panelUrl: null}).side();

// 5. Storage blocked.
storageBlocked = true;
newDocument();
let blockedThrew = false;
let blockedSide: string | null = null;
try {
    const blocked = installDock({panelUrl: null});
    blockedSide = blocked.side();
    blocked.setSide("right");
    blockedSide += "," + blocked.side();
} catch {
    blockedThrew = true;
}
storageBlocked = false;

// 6. No panel URL.
newDocument();
const noPanel = installDock({panelUrl: null});
noPanel.toggle();
const framesWithoutPanel = parts(noPanel.element as unknown as StubElement).drawer.find("iframe").length;

// 7. A panel that is not a page (the WASM host's): the frame is given its document, and the host is told once, after the
// frame is attached — a frame's window exists only then.
newDocument();
const framed: string[] = [];
const wasm = installDock({
    panelUrl: null,
    frameDocument: "<!DOCTYPE html><title>panel</title>",
    onFrame: (frame: HTMLIFrameElement) =>
        framed.push((frame as unknown as StubElement).parentNode ? "attached" : "detached"),
});
wasm.toggle();
wasm.toggle();
wasm.toggle();
const wasmFrames = parts(wasm.element as unknown as StubElement).drawer.find("iframe");
const wasmFrameDocument = (wasmFrames[0] as unknown as {srcdoc?: string} | undefined)?.srcdoc ?? null;
const wasmFrameSrc = wasmFrames[0]?.src ?? null;

process.stdout.write(JSON.stringify({
    mountedAfterBody, managed, closedAtStart, framesAtStart, sideAtStart, shortcutListenerIsCapture,
    openAfterPill, frameCount: frames.length, frameSrc,
    closedByShortcut, shortcutSwallowed, focusReturnedToPill, plainIgnored, reopenedByMac, framesAfterReopen,
    storedSide, drawerSide, rightPressed, sideOnNextInstall,
    blockedThrew, blockedSide,
    framesWithoutPanel,
    wasmFrameCount: wasmFrames.length, wasmFrameDocument, wasmFrameSrc, framed
}) + "\n");
