// The page behind an open overlay (data-rask-lock).
//
// A popover or dialog carrying data-rask-lock holds the document still while it is open: no scrolling behind
// it, and — unless it says data-rask-lock="scroll" — no pointer either, so a press outside only closes it and
// never also presses what was underneath. The scrollbar's gutter is kept, so the page does not jump sideways.
// These are the three declarations Flux UI puts on <html> under an open select, dropdown and popover
// (overflow: hidden; pointer-events: none; scrollbar-gutter: stable), and the two it puts there under a modal.
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

const RULES = "html[" + LOCKED + "]{overflow:hidden;scrollbar-gutter:stable}"
    + "html[" + LOCKED + "=all]{pointer-events:none}"
    + "[" + LOCK + "]{pointer-events:auto}";

let sheet = false;
let locked = "";

// A <dialog popover> shown as a popover is open and its `open` is false: both are asked.
function isOpen(el: Element): boolean {
    return (el instanceof HTMLDialogElement && el.open) || isShown(el);
}

function apply(doc: Document, state: string): void {
    if (state === locked) {
        return;
    }
    locked = state;
    const root = doc.documentElement;
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
    if (state) {
        own(root, LOCKED);
        root.setAttribute(LOCKED, state);
    } else {
        root.removeAttribute(LOCKED);
        disown(root, LOCKED);
    }
    if (!sheet) {
        root.style.overflow = state ? "hidden" : "";
        root.style.setProperty("scrollbar-gutter", state ? "stable" : "");
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
