// The page's side of the Errors tab: the count on the pill, and the ways in — the pill's dot, and an "Open in DevTools"
// button the devtools add to the corner dev-error overlay.
//
// The overlay itself belongs to the runtime and ships in every Release page, so nothing of the devtools goes into it
// there: this module finds the overlay once the runtime has built it, and adds its button from the outside.

import {CHANNEL, type FrameMessage} from "../rask-devtools-frame-protocol.js";
import type {DockHandle} from "./dock.js";

/** The attribute that marks the button the devtools added to the overlay, so it is added once. */
export const OVERLAY_BUTTON_ATTRIBUTE = "data-rask-devtools-open";

export interface ErrorsLink {
    /** Opens the drawer on the Errors tab, as soon as the panel is there to take the request. */
    show(): void;
    /** The panel said something: it is listening, so a request waiting for it can go now. */
    heardFromPanel(): void;
}

export function installErrorsLink(dock: DockHandle, post: (message: FrameMessage) => void): ErrorsLink {
    let wanted = false;
    let panelUp = false;

    const flush = () => {
        if (!wanted || !panelUp) return;
        wanted = false;
        post({channel: CHANNEL, kind: "show-errors"});
    };

    const link: ErrorsLink = {
        show() {
            if (!dock.isOpen()) dock.toggle();
            wanted = true;
            flush();
        },
        heardFromPanel() {
            panelUp = true;
            flush();
        },
    };

    const decorate = () => {
        const overlay = document.querySelector("[data-rask-dev-error]");
        if (overlay) addOverlayButton(overlay, () => link.show());
    };

    // The runtime appends its overlay to <html> the first time something goes wrong, and reuses it after that.
    if (typeof MutationObserver === "function") {
        new MutationObserver(decorate).observe(document.documentElement, {childList: true});
    }
    decorate();

    return link;
}

/** Adds the "Open in DevTools" button to the overlay's bar, beside its own buttons, unless it is already there. */
export function addOverlayButton(overlay: Element, onOpen: () => void): boolean {
    if (overlay.querySelector("[" + OVERLAY_BUTTON_ATTRIBUTE + "]")) return false;
    const bar = overlay.querySelector(".rask-deverr__bar");
    if (!bar) return false;

    const button = document.createElement("button");
    button.type = "button";
    // The overlay's own button class, so it looks like the buttons beside it.
    button.className = "rask-deverr__btn";
    button.setAttribute(OVERLAY_BUTTON_ATTRIBUTE, "");
    button.textContent = "Open in DevTools";
    button.addEventListener("click", onOpen);

    const first = bar.querySelector(".rask-deverr__btn");
    if (first) {
        bar.insertBefore(button, first);
    } else {
        bar.appendChild(button);
    }
    return true;
}
