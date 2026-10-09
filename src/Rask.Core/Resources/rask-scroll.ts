// Scroll on a press (data-rask-scroll-to).
//
// A pager under a long table changes the rows and leaves the reader at the foot of the new page. An element
// carrying data-rask-scroll-to="<selector>" brings what that selector names into view whenever a button or a
// link inside it is pressed (Flux UI's `scroll-to` on its pagination). Delegated, so it holds for a pager
// rendered later, and it never stops the press: the handler or the link still runs, after it.

import {listen, near, page} from "./rask-owned.js";

const ATTR = "data-rask-scroll-to";

/** What `host` asks to have scrolled into view, or null: nothing by that selector, or not a selector at all. */
export function scrollTarget(doc: ParentNode, host: Element): Element | null {
    try {
        return doc.querySelector(host.getAttribute(ATTR) || "body");
    } catch (error) {
        return null;
    }
}

listen("click", function (e) {
    const control = near(e.target, "button, a");
    const host = control && control.closest("[" + ATTR + "]");
    const target = host && page ? scrollTarget(page, host) : null;
    if (target) {
        target.scrollIntoView();
    }
});
