// The panel's side of the page overlays, shared by both panels: the Server panel page loads it as its own script, and
// the WASM panel frame bundles it into its frame client.
//
// It never draws anything and never talks to the app. It reads what the Tree tab rendered — the place of the row under
// the pointer, the anchors while picking — and posts it to the page, where the devtools host draws the box; and it hands
// a pick the page reports back to the tab as a keydown on the tab's hidden element, the one event both panels forward to
// their session with a value. A hover therefore costs no round trip: the row already says where its node is.

import {isToggleShortcut} from "../host/dock.js";
import {asFrameMessage, CHANNEL, type FrameMessage} from "../rask-devtools-frame-protocol.js";

/** The prefix of the keydown `key` a pick is reported as; `DevToolsTreeTab.PickKeyPrefix` on the C# side. */
export const PICK_KEY_PREFIX = "pick:";

export interface PanelClientOptions {
    /** Posts a message to the page. */
    readonly post: (message: FrameMessage) => void;
    /** Whether a message event came from the page this panel belongs to. */
    readonly fromPage: (e: MessageEvent) => boolean;
}

/** Binds the panel's listeners to this document. Call once. */
export function installPanelClient(options: PanelClientOptions): void {
    const {post, fromPage} = options;
    let lastAt: string | null = null;
    let lastAnchors: string | null = null;

    const highlight = (at: string | null, label: string | null) => {
        if (at === lastAt) return;
        lastAt = at;
        post({channel: CHANNEL, kind: "highlight", at, label});
    };

    // Delegated, so rows the virtualized tree renders later are covered without binding anything to them.
    document.addEventListener("pointerover", (e: PointerEvent) => {
        const target = e.target instanceof Element ? e.target : null;
        const row = target ? target.closest(".ui-tree-row") : null;
        const place = row ? row.querySelector("[data-rask-devtools-at]") : null;
        highlight(
            place ? place.getAttribute("data-rask-devtools-at") : null,
            place ? place.getAttribute("data-rask-devtools-label") : null);
    });

    // The pointer leaving the panel's document altogether: no pointerover follows inside it to clear the box.
    document.documentElement.addEventListener("pointerleave", () => highlight(null, null));

    const syncAnchors = () => {
        const el = document.querySelector("[data-rask-devtools-anchors]");
        const anchors = el ? el.getAttribute("data-rask-devtools-anchors") : null;
        if (anchors === lastAnchors) return;
        lastAnchors = anchors;
        post({channel: CHANNEL, kind: "pick", anchors});
    };

    // The Tree tab's render is what starts, updates and stops a pick; the panel's runtime applies it to this document.
    if (typeof MutationObserver === "function") {
        new MutationObserver(syncAnchors).observe(document.documentElement, {
            subtree: true,
            childList: true,
            attributes: true,
            attributeFilter: ["data-rask-devtools-anchors"],
        });
    }
    syncAnchors();

    window.addEventListener("message", (e: MessageEvent) => {
        if (!fromPage(e)) return;
        const message = asFrameMessage(e.data);
        if (!message) return;
        if (message.kind === "picked" && typeof message.id === "string") {
            reportPick(message.id);
        } else if (message.kind === "pick-cancelled") {
            reportPick("cancel");
        }
    });

    // With focus inside the panel — where it is right after pressing Pick — the page never sees a keystroke. The shortcut
    // is handed up; Esc during a pick ends it here, and the anchors leaving the tab's render stop the page's picker.
    window.addEventListener("keydown", (e: KeyboardEvent) => {
        if (isToggleShortcut(e)) {
            e.preventDefault();
            e.stopImmediatePropagation();
            post({channel: CHANNEL, kind: "toggle"});
        } else if (e.key === "Escape" && lastAnchors !== null) {
            e.preventDefault();
            reportPick("cancel");
        }
    }, true);
}

// A keydown on the tab's hidden element: the runtime's shared key forwarding sends it to the tab with its key.
function reportPick(value: string): void {
    const el = document.querySelector("[data-rask-devtools-picked]");
    if (!el) return;
    el.dispatchEvent(new KeyboardEvent("keydown", {key: PICK_KEY_PREFIX + value, bubbles: true}));
}
