// The page's side of the conversation with the panel frame about the overlays, shared by both hosts: a hover boxes the
// row's nodes, a pick request turns the picker on, and what the picker chooses goes back to the panel.

import {asFrameMessage, CHANNEL, type FrameMessage} from "../rask-devtools-frame-protocol.js";
import {parseAnchors} from "./anchors.js";
import type {DockHandle} from "./dock.js";
import type {Overlay} from "./overlay.js";

export interface PanelBridge {
    /** Handles one message from the panel frame; true when it was an overlay message this bridge acted on. */
    handle(message: FrameMessage): boolean;
    /** The drawer closed: clear the box, and end a pick in progress as cancelled so the panel's button lets go. */
    closed(): void;
}

export function createBridge(dock: DockHandle, overlay: Overlay, post: (message: FrameMessage) => void): PanelBridge {
    const cancel = () => post({channel: CHANNEL, kind: "pick-cancelled"});

    return {
        handle(message) {
            switch (message.kind) {
                case "highlight":
                    if (typeof message.at === "string") {
                        overlay.show(message.at, typeof message.label === "string" ? message.label : null);
                    } else {
                        overlay.hide();
                    }
                    return true;
                case "pick":
                    if (typeof message.anchors === "string") {
                        overlay.pick(parseAnchors(message.anchors), id => post({channel: CHANNEL, kind: "picked", id}), cancel);
                    } else {
                        overlay.stopPicking();
                    }
                    return true;
                case "toggle":
                    dock.toggle();
                    return true;
                default:
                    return false;
            }
        },
        closed() {
            const picking = overlay.isPicking();
            overlay.stopPicking();
            overlay.hide();
            if (picking) cancel();
        },
    };
}

/**
 * Listens for the panel frame's messages on this window: only from `frameWindow()`, and only in `origin` when one is
 * given (a Server panel is a page of the app's own origin; a WASM srcdoc frame may report "null"). `other` receives the
 * messages the bridge does not handle.
 */
export function listenToPanel(
    frameWindow: () => Window | null,
    origin: string | null,
    bridge: () => PanelBridge | null,
    other?: (message: FrameMessage) => void,
): void {
    window.addEventListener("message", (e: MessageEvent) => {
        const source = frameWindow();
        if (!source || e.source !== source) return;
        if (origin !== null && e.origin !== origin) return;
        const message = asFrameMessage(e.data);
        if (!message) return;
        if (bridge()?.handle(message)) return;
        other?.(message);
    });
}
