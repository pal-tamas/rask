// What a navigation's page does for a reader who cannot see it arrive, the same on both hosts: it is announced,
// and keyboard focus moves into it.
//
//   * FORWARD (a pushed entry: a link, `Go()`): the page scrolls to the top, or to the `#fragment` the link
//     carried; focus goes to that fragment's element, else to the page's own `autofocus` field
//     (rask-autofocus.ts), else into its `<main>` (or `[role=main]`, or its first `<h1>`) — so a keyboard user
//     goes on from the new page rather than from the link at the top of the document that is gone.
//   * BACK AND FORWARD (popstate): the scroll is the browser's to restore and focus is moved only to an
//     `autofocus` field that arrived; the page is still announced.
//   * Any other replaced address (a query the page set, a redirect) is the same page, and says nothing.
//
// The announcement is the new `<title>`, through one polite live region — the visible page changing is silent
// to a screen reader.

import {focusArrived} from "./rask-autofocus.js";

const ANNOUNCER = "rask-route-announcer";

// How long the region stays empty before the title is written: some screen readers coalesce a clear and a
// write in the same task into no change at all.
const ANNOUNCE_AFTER_MS = 50;

let announcer: HTMLElement | null = null;

// A Back or Forward was heard, and the reply it asks for has not been shown yet.
let poppedState = false;

/** Puts the live region on the page. Each host calls it once, as it starts. */
export function installRouteAnnouncer(): HTMLElement {
    if (announcer && announcer.isConnected) return announcer;
    const el = document.createElement("div");
    el.setAttribute("data-rask-managed", "");
    el.setAttribute("aria-live", "polite");
    el.setAttribute("aria-atomic", "true");
    el.className = ANNOUNCER;
    el.style.cssText = "position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;"
        + "clip:rect(0 0 0 0);white-space:nowrap;border:0;";
    document.documentElement.appendChild(el);
    announcer = el;
    return el;
}

/** The reader went Back or Forward: the reply that answers it is a navigation to announce. */
export function backOrForward(): void {
    poppedState = true;
}

// The title still to be written: a navigation that follows inside the pause replaces it, so an older page's
// title is never written over a newer one's.
let announcing: ReturnType<typeof setTimeout> | null = null;

function announce(): void {
    const region = installRouteAnnouncer();
    region.textContent = "";
    const title = document.title || location.pathname;
    if (announcing !== null) clearTimeout(announcing);
    announcing = setTimeout(function () {
        announcing = null;
        region.textContent = title;
    }, ANNOUNCE_AFTER_MS);
}

function anchorOf(hash: string): HTMLElement | null {
    if (!hash || hash.length < 2) return null;
    try {
        return document.querySelector<HTMLElement>(hash)
            || document.getElementById(decodeURIComponent(hash.slice(1)));
    } catch {
        // Not a selector (`#1`), and not a well-formed escape either.
        return document.getElementById(hash.slice(1));
    }
}

/** Where focus goes on a page that names no place for it: its main content, else its heading. */
export function routeFocusTarget(): HTMLElement | null {
    return document.querySelector<HTMLElement>("main, [role=main]") || document.querySelector<HTMLElement>("h1");
}

// Focus `target`, made programmatically focusable for as long as it holds focus. The tabindex is added only
// for the focus, and backed out if focus did not take, so a detached target cannot leave one on the page's DOM.
function focusInto(target: HTMLElement): void {
    const added = !target.hasAttribute("tabindex");
    if (added) target.setAttribute("tabindex", "-1");
    try {
        target.focus({preventScroll: true});
    } catch {
        // The target went away between the query and the focus.
    }
    if (document.activeElement !== target) {
        if (added) target.removeAttribute("tabindex");
        return;
    }
    if (added) {
        target.addEventListener("blur", function () { target.removeAttribute("tabindex"); }, {once: true});
    }
}

/**
 * A frame is on the page. `history` is the frame's own (none for a render of the page that is open); `hash` is
 * the `#fragment` the link that asked for it carried, which never leaves the browser.
 */
export function navigationShown(history: {action?: string} | null | undefined, hash: string): void {
    if (!history || history.action === "replace") {
        const popped = !!history && poppedState;
        if (history) poppedState = false;
        focusArrived(history);
        if (popped) announce();
        return;
    }

    poppedState = false;
    const anchor = anchorOf(hash);
    if (anchor) anchor.scrollIntoView();
    else window.scrollTo(0, 0);
    announce();
    if (focusArrived(history, !!anchor)) return;
    const target = anchor || routeFocusTarget();
    if (target) focusInto(target);
}
