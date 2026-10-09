// The page behind an open overlay (data-rask-lock).
//
// A popover or dialog carrying data-rask-lock holds the document still while it is open: no scrolling behind
// it, and — unless it says data-rask-lock="scroll" — no pointer either, so a press outside only closes it and
// never also presses what was underneath. The scrollbar's gutter is kept, so the page does not jump sideways.
// These are the three declarations Flux UI puts on <html> under an open select, dropdown and popover
// (overflow: hidden; pointer-events: none; scrollbar-gutter: stable), and the two it puts there under a modal.
//
// THE GUTTER IS KEPT ONLY WHERE THERE WAS ONE. `scrollbar-gutter: stable` reserves a scrollbar's width whether
// or not a scrollbar was taking any: on a page too short to scroll, and in a browser run with its scrollbars
// hidden (every headless one), it narrows the page by 15 px that were in use. The text wraps again, what is
// above the reader's place grows by a line or two, and scroll anchoring moves window.scrollY to follow — the
// jump of a line's height measured under an open select, date picker and modal. So the width the scrollbar
// takes is read as the lock goes on, and the gutter is asked for (data-rask-gutter on <html>) only when that
// is more than nothing. Where a scrollbar shows, the declarations are Flux's; where none does, nothing moves.
//
// COUNTED by asking, not by keeping score: every time something that could change the answer happens — a
// toggle or close event, or a node leaving the page while locked — the open [data-rask-lock] elements are
// counted again. A render that removes an open panel, a navigation that replaces the page and two menus
// open at once therefore all end in the right state, and nothing can leave the page locked for good.
//
// The lock is one attribute on <html>, data-rask-locked, and one constructed stylesheet: a stylesheet made in
// script is not an inline style, so a strict Content-Security-Policy has nothing to object to, and a render
// that rewrites <html>'s own style attribute cannot undo it. The panel itself stays usable through the same
// sheet. Where constructed stylesheets are missing the declarations go on <html> directly.

import {disown, isShown, listen, own, page} from "./rask-owned.js";

const LOCK = "data-rask-lock";
const LOCKED = "data-rask-locked";

const GUTTER = "data-rask-gutter";

const RULES = "html[" + LOCKED + "]{overflow:hidden}"
    + "html[" + GUTTER + "]{scrollbar-gutter:stable}"
    + "html[" + LOCKED + "=all]{pointer-events:none}"
    + "[" + LOCK + "]{pointer-events:auto}";

let sheet = false;
let locked = "";

// A <dialog popover> shown as a popover is open and its `open` is false: both are asked.
function isOpen(el: Element): boolean {
    return (el instanceof HTMLDialogElement && el.open) || isShown(el);
}

function mark(root: Element, name: string, value: string | null): void {
    if (value === null) {
        root.removeAttribute(name);
        disown(root, name);
    } else {
        own(root, name);
        root.setAttribute(name, value);
    }
}

function apply(doc: Document, state: string): void {
    if (state === locked) {
        return;
    }
    const root = doc.documentElement;
    const view = doc.defaultView;
    // Asked as the lock goes on, while the page is still as the reader has it: the window is wider than what
    // the page is laid out in exactly when a scrollbar takes room beside it. Kept until the lock lets go.
    const gutter = !!state && (locked ? root.hasAttribute(GUTTER) : !!view && view.innerWidth > root.clientWidth);
    locked = state;
    const adopted = (doc as Document & { adoptedStyleSheets?: CSSStyleSheet[] }).adoptedStyleSheets;
    if (!sheet && adopted && typeof CSSStyleSheet === "function") {
        try {
            const css = new CSSStyleSheet();
            css.replaceSync(RULES);
            (doc as Document & { adoptedStyleSheets: CSSStyleSheet[] }).adoptedStyleSheets = adopted.concat(css);
            sheet = true;
        } catch (e) {
            // no constructable stylesheets here: the inline path below
        }
    }
    mark(root, LOCKED, state || null);
    mark(root, GUTTER, gutter ? "" : null);
    if (!sheet) {
        root.style.overflow = state ? "hidden" : "";
        root.style.setProperty("scrollbar-gutter", gutter ? "stable" : "");
        root.style.pointerEvents = state === "all" ? "none" : "";
    }
}

function recount(doc: Document, closing?: Element): void {
    let state = "";
    const holders = doc.querySelectorAll("[" + LOCK + "]");
    for (let i = 0; i < holders.length; i++) {
        if (holders[i] !== closing && isOpen(holders[i])) {
            // One overlay that takes the pointer is enough for the page to lose it.
            state = holders[i].getAttribute(LOCK) === "scroll" && state !== "all" ? "scroll" : "all";
        }
    }
    apply(doc, state);
}

if (page) {
    const doc = page;
    const onChange = function (e: Event): void {
        if (e.target instanceof Element && e.target.hasAttribute(LOCK)) {
            recount(doc);
        }
    };
    // None of them bubbles; all are caught on the way down. beforetoggle is the one that runs in the same task
    // as the open or the close — Flux's lock is there and gone that promptly — but it is cancelable, so a
    // count afterwards settles it.
    listen("toggle", onChange, true);
    listen("close", onChange, true);
    listen("beforetoggle", function (e) {
        const el = e.target;
        if (!(el instanceof Element) || !el.hasAttribute(LOCK)) {
            return;
        }
        if ((e as ToggleEvent).newState === "open") {
            if (!locked) {
                apply(doc, el.getAttribute(LOCK) === "scroll" ? "scroll" : "all");
            }
        } else {
            recount(doc, el); // it is still open here, and about to not be
        }
        // By then it has opened or closed — or the page refused the event, and it has not.
        window.setTimeout(function () { recount(doc); }, 0);
    }, true);

    if (typeof MutationObserver === "function") {
        // Only while locked does a node leaving the page matter, and only then is anything looked at.
        new MutationObserver(function (records) {
            if (!locked) {
                return;
            }
            for (const record of records) {
                if (record.removedNodes.length) {
                    recount(doc);
                    return;
                }
            }
        }).observe(doc.documentElement, {subtree: true, childList: true});
    }
}
