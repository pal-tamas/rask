// The devtools panel's frame client on a WASM page. It runs inside the drawer's srcdoc iframe, in that document's own
// realm, and nowhere else: the panel's C# renders in the app's .NET runtime, the page posts each frame in, and this
// applies it to the iframe's document — the same diff and morph the WASM runtime applies to the page — and posts the
// panel's events back out.
//
// A frame client, not a second runtime: a same-origin iframe shares the page's main thread, so a second .NET boot would
// stall the app, and it would re-run the app's Program.cs besides.
//
// Deliberately smaller than rask.wasm.ts: the panel has no history, no scoped CSS or JS, no JS invokes and no downloads.

import {applyDiff, type DiffOp} from "../../Rask.Core/Resources/rask-dom.js";
import {morph} from "../../Rask.Core/Resources/rask-morph.js";
import {setHost} from "../../Rask.Core/Resources/rask-host.js";
import {isToggleShortcut} from "./host/dock.js";
import {CHANNEL, type FrameMessage} from "./rask-devtools-frame-protocol.js";

// For its side effect: the shared click, change, submit and key listeners, bound to THIS document.
import "../../Rask.Core/Resources/rask-events.js";

const decoder = new TextDecoder();

// Frames apply strictly in order, as the page runtime's do.
let queue: Promise<void> = Promise.resolve();

function post(message: FrameMessage): void {
    // The page accepts only messages whose source is this frame's window, so the target origin adds nothing a srcdoc
    // frame could state reliably: it inherits the page's origin, but reports "null" in some browsers.
    window.parent.postMessage(message, "*");
}

function apply(bytes: Uint8Array): void {
    let reply: RaskFrameReply;
    try {
        reply = JSON.parse(decoder.decode(bytes)) as RaskFrameReply;
    } catch (e) {
        console.error("[Rask DevTools] the panel sent a frame that could not be read", e);
        return;
    }

    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        if (typeof reply.head === "string") {
            const head = new DOMParser().parseFromString(reply.head, "text/html").head;
            if (head) morph(document.head, head);
        }
        applyDiff(reply.ops as DiffOp[], Array.isArray(reply.names) ? reply.names : undefined);
        return;
    }

    if (typeof reply.html === "string" && reply.html.length > 0) {
        morph(document.documentElement, new DOMParser().parseFromString(reply.html, "text/html").documentElement);
    }
}

setHost({
    send: (payload: unknown) => post({channel: CHANNEL, kind: "event", payload}),
    inRoot: (el: Node | null) => !!el && !!document.body && document.body.contains(el),
});

window.addEventListener("message", (e: MessageEvent) => {
    // Only the page that made this frame may drive it.
    if (e.source !== window.parent) return;
    const data = e.data as Partial<FrameMessage> | null;
    if (!data || data.channel !== CHANNEL || data.kind !== "frame") return;
    const bytes = (data as {bytes?: unknown}).bytes;
    if (!(bytes instanceof Uint8Array)) return;
    queue = queue.then(() => apply(bytes), () => apply(bytes));
});

// With focus inside the panel, the page's own shortcut listener never sees the keystroke, so it is handed up.
window.addEventListener("keydown", (e: KeyboardEvent) => {
    if (!isToggleShortcut(e)) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    post({channel: CHANNEL, kind: "toggle"});
}, true);

post({channel: CHANNEL, kind: "ready"});
