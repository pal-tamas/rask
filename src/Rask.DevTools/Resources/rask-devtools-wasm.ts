// The devtools on a WASM page: the module Rask.DevTools' browser face imports through [JSImport]. The Server host loads
// rask-devtools-host.js with a <script> tag instead; this is the same pill and drawer, framing a panel whose C# runs in
// the app's own runtime rather than on a server.
//
// Imported only by a Debug build on a page served from this machine — the C# decides both before importing it — and
// never part of rask.wasm.js.

import {installDock} from "./host/dock.js";
import {CHANNEL, type FrameMessage} from "./rask-devtools-frame-protocol.js";

let frameWindow: Window | null = null;
let ready = false;
const pending: Uint8Array[] = [];

function postFrame(bytes: Uint8Array): void {
    const message: FrameMessage = {channel: CHANNEL, kind: "frame", bytes};
    // Transferred, not copied: the bytes were copied once out of .NET memory already and are not used here again.
    frameWindow!.postMessage(message, "*", [bytes.buffer]);
}

/**
 * Installs the pill and the drawer. `frameDocument` is the panel frame's srcdoc. `onOpen` runs once, the first time the
 * drawer opens, so a page whose tools stay closed never renders the panel. `onEvent` receives each panel event as JSON.
 */
export function install(frameDocument: string, onOpen: () => void, onEvent: (json: string) => void): void {
    if (window.__raskDevtoolsHost) return;

    const dock = installDock({
        panelUrl: null,
        frameDocument,
        onFrame: (frame: HTMLIFrameElement) => {
            frameWindow = frame.contentWindow;
            onOpen();
        },
    });
    window.__raskDevtoolsHost = {version: 1, dock};

    window.addEventListener("message", (e: MessageEvent) => {
        // Only the panel frame this module created; anything else on the page speaking the same shape is ignored.
        if (!frameWindow || e.source !== frameWindow) return;
        const data = e.data as Partial<FrameMessage> | null;
        if (!data || data.channel !== CHANNEL) return;

        switch (data.kind) {
            case "ready":
                ready = true;
                for (const bytes of pending.splice(0)) postFrame(bytes);
                break;
            case "event":
                onEvent(JSON.stringify((data as {payload?: unknown}).payload ?? null));
                break;
            case "toggle":
                dock.toggle();
                break;
        }
    });
}

/**
 * One frame from the panel session. `view` is a transient view over .NET memory, valid only for this call, so it is
 * copied before anything else happens; a frame that arrives before the frame document is listening waits for its ready
 * signal.
 */
export function deliver(view: {slice(): Uint8Array}): void {
    const bytes = view.slice();
    if (!frameWindow || !ready) {
        pending.push(bytes);
        return;
    }
    postFrame(bytes);
}
