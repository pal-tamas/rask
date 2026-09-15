// The page's side of the Errors tab: the count on the pill, what fails on the page itself — a script's uncaught error, a
// promise rejected with nobody to catch it, an island that failed — and the ways in: the pill's dot, and an "Open in
// DevTools" button the devtools add to the corner dev-error overlay.
//
// A page failure is the page's to report, since no server hears of it. It is counted on the pill at once; the panel lists
// it once it is listening, so up to PAGE_ERROR_LIMIT of them wait for it. console.error is not wrapped: an app's own
// logging is not a failure.
//
// The overlay itself belongs to the runtime and ships in every Release page, so nothing of the devtools goes into it
// there: this module finds the overlay once the runtime has built it, and adds its button from the outside.

import {nodePath} from "../../../Rask.Core/Resources/rask-dom-path.js";
import {CHANNEL, type FrameMessage, type PageErrorReport} from "../rask-devtools-frame-protocol.js";
import type {DockHandle} from "./dock.js";

/** How many page failures wait for a panel that has not opened yet; the oldest go first. */
export const PAGE_ERROR_LIMIT = 50;

/** How much of a stack a report carries. */
export const STACK_LIMIT = 4096;

/** The attribute that marks the button the devtools added to the overlay, so it is added once. */
export const OVERLAY_BUTTON_ATTRIBUTE = "data-rask-devtools-open";

export interface ErrorsLink {
    /** Opens the drawer on the Errors tab, as soon as the panel is there to take the request. */
    show(): void;
    /** The panel said something: it is listening, so a request, and the failures, waiting for it can go now. */
    heardFromPanel(): void;
    /** The panel's count of unseen errors; the pill shows it with the failures not handed to the panel yet. */
    setPanelCount(count: number): void;
    /** A failure on the page: handed to the panel, or held for it. */
    record(report: PageErrorReport): void;
    /** The island runtime's hook: an island failed. */
    island(phase: string, element: Element, name: string | null, error: unknown): void;
}

/** A report of a thrown or rejected value, whatever it is. */
export function describeFailure(
    kind: PageErrorReport["kind"], value: unknown, fallbackTitle: string, fallbackMessage: string, at: number,
): PageErrorReport {
    const error = value !== null && typeof value === "object" ? value as {name?: unknown; message?: unknown; stack?: unknown} : null;
    const stack = error && typeof error.stack === "string" ? error.stack.slice(0, STACK_LIMIT) : null;
    return {
        kind,
        title: error && typeof error.name === "string" && error.name ? error.name : fallbackTitle,
        message: error && typeof error.message === "string"
            ? error.message
            : value === undefined || value === null || error ? fallbackMessage : String(value),
        stack,
        at,
        island: null,
        phase: null,
        place: null,
    };
}

/** `path|slot|1` for an element, the place format the Tree tab writes, or null when it is not in the document. */
export function placeOf(element: Element): string | null {
    const path = nodePath(element);
    if (!path || path.length === 0) return null;
    return path.slice(0, -1).join(".") + "|" + path[path.length - 1] + "|1";
}

export function installErrorsLink(dock: DockHandle, post: (message: FrameMessage) => void): ErrorsLink {
    let wanted = false;
    let panelUp = false;
    let panelCount = 0;
    const waiting: PageErrorReport[] = [];

    const alert = () => dock.setAlert(panelCount + waiting.length);

    const flush = () => {
        if (!panelUp) return;
        if (waiting.length > 0) {
            for (const report of waiting.splice(0)) post({channel: CHANNEL, kind: "page-error", report});
            alert();
        }
        if (wanted) {
            wanted = false;
            post({channel: CHANNEL, kind: "show-errors"});
        }
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
        setPanelCount(count) {
            panelCount = Math.max(0, count);
            alert();
        },
        record(report) {
            if (panelUp) {
                post({channel: CHANNEL, kind: "page-error", report});
                return;
            }
            waiting.push(report);
            if (waiting.length > PAGE_ERROR_LIMIT) waiting.shift();
            alert();
        },
        island(phase, element, name, error) {
            const report = describeFailure("island", error, "Error", String(error), Date.now());
            link.record({...report, island: name, phase, place: placeOf(element)});
        },
    };

    // Bubble phase on the window: a script's uncaught error arrives here, and a resource that failed to load does not.
    window.addEventListener("error", (e: ErrorEvent) => {
        const report = describeFailure("page", e.error, "Error", e.message || "A script failed.", Date.now());
        // A script from another origin says only "Script error." — where it came from is all there is to add.
        link.record(report.stack || !e.filename ? report : {...report, stack: `${e.filename}:${e.lineno}:${e.colno}`});
    });
    window.addEventListener("unhandledrejection", (e: PromiseRejectionEvent) =>
        link.record(describeFailure("page", e.reason, "Unhandled rejection", "A promise was rejected and nothing caught it.",
            Date.now())));

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
