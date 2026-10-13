// A popover toggled from something that is not a <button popovertarget>, and aria-expanded kept true to it.
//
//   * data-rask-toggle="<popover id>" on any element: a click on it, and Enter or Space while it or something
//     inside it that is not a control of its own has focus, toggle that popover. Measured on Flux UI's toggleable tooltip: hovering does nothing; a click opens it
//     and the next closes it; Enter and Space do the same; Escape, a press outside both, and Tab away close
//     it; a press inside the bubble does not. A popover="auto" gets the last three from the platform. A
//     popover="manual" — what Flux's is — gets them here, for as long as this hook is what opened it.
//     Focus and tabindex are the component's: an element that cannot be focused is toggled by the pointer only.
//     A press in a field inside the toggle is typing there and toggles nothing — Flux's typed date and time
//     triggers open from everything in them but their fields.
//   * aria-expanded follows the popover: whenever one opens or closes — however it was asked to — every
//     element that invokes it (popovertarget, commandfor, data-rask-toggle) AND already carries aria-expanded
//     is rewritten. The attribute is never added where the render did not put it.
//   * data-open follows a popover that asks for it (data-rask-open-mark): there while it shows, in the task
//     that opens it, and gone in the one that closes it — as it is on a dialog (rask-overlay.ts), and as Flux
//     UI writes it on an open panel, whose own examples lay the panel out by it (`data-open:flex`). A render
//     cannot say it in time: the page hears of a press a round trip after the browser has opened the panel.
//     With a value — data-rask-open-mark="<popover id>" — it is on any OTHER element while that popover shows:
//     the dropdown around a trigger and its panel, the trigger itself, the area a context menu belongs to. So
//     nothing about a panel being open is the page's to draw, and opening one asks the server nothing.

import {disown, isShown, listen, named, near, own, page, setShown} from "./rask-owned.js";

const TOGGLE = "data-rask-toggle";
const MARK = "data-rask-open-mark";

// Held against the render while it is there: no render carries it.
function mark(panel: Element, open: boolean): void {
    if (open === panel.hasAttribute("data-open")) {
        return;
    }
    if (open) {
        own(panel, "data-open");
        panel.setAttribute("data-open", "");
    } else {
        panel.removeAttribute("data-open");
        disown(panel, "data-open");
    }
}
const FIELD = "input, textarea, select, [contenteditable]";

// The panel itself when it asks, and everything that names it.
function markAll(doc: Document, panel: HTMLElement, open: boolean): void {
    if (panel.getAttribute(MARK) === "") {
        mark(panel, open);
    }
    if (!panel.id) {
        return;
    }
    const naming = doc.querySelectorAll("[" + MARK + "]");
    for (let i = 0; i < naming.length; i++) {
        if (naming[i].getAttribute(MARK) === panel.id) {
            mark(naming[i], open);
        }
    }
}

if (page) {
    const doc = page;
    // The manual popovers this hook opened, and what opened each: the ones it also has to close.
    const opened = new Map<HTMLElement, HTMLElement>();
    // Whether the popover was showing when the press began: a press on the invoker of a popover="auto"
    // light-dismisses it before the click arrives, and the click must not open it again.
    let shownAtPress: HTMLElement | null = null;

    function set(invoker: HTMLElement, panel: HTMLElement, open: boolean): void {
        setShown(panel, open);
        if (open && panel.getAttribute("popover") === "manual" && isShown(panel)) {
            opened.set(panel, invoker);
        } else {
            opened.delete(panel);
        }
    }

    function closeUnless(inside: EventTarget | null): void {
        for (const [panel, invoker] of Array.from(opened)) {
            if (!(inside instanceof Node) || !(panel.contains(inside) || invoker.contains(inside))) {
                set(invoker, panel, false);
            }
        }
    }

    listen("pointerdown", function (e) {
        const invoker = near(e.target, "[" + TOGGLE + "]");
        const panel = invoker ? named(invoker, TOGGLE) : null;
        shownAtPress = panel && isShown(panel) ? panel : null;
        closeUnless(e.target);
    }, {capture: true, passive: true});

    listen("click", function (e) {
        const invoker = near(e.target, "[" + TOGGLE + "]");
        const panel = invoker ? named(invoker, TOGGLE) : null;
        // A click inside the popover (it may be the invoker's own child) is using it.
        if (!invoker || !panel || (e.target instanceof Node && panel.contains(e.target))) {
            return;
        }
        // A field inside the toggle (not the toggle itself) takes the press for its caret.
        const field = near(e.target, FIELD);
        if (field && field !== invoker && invoker.contains(field)) {
            return;
        }
        // detail 0 is a click the keyboard made (Enter on a button): no press came before it.
        const wasShown = (e as MouseEvent).detail === 0 ? isShown(panel) : shownAtPress === panel;
        shownAtPress = null;
        set(invoker, panel, !wasShown);
    });

    listen("keydown", function (e) {
        if (e.key === "Escape") {
            closeUnless(null);
            return;
        }
        const t = e.target;
        const invoker = near(t, "[" + TOGGLE + "]");
        const panel = invoker ? named(invoker, TOGGLE) : null;
        if ((e.key !== "Enter" && e.key !== " ") || !invoker || !panel || e.ctrlKey || e.altKey || e.metaKey
            || panel.contains(t as Node)
            // These press themselves on the key (and the click above does the rest), or are being typed into.
            || near(t, "button, a[href], input, textarea, select, summary, [contenteditable]")) {
            return;
        }
        e.preventDefault(); // Space would scroll the page
        set(invoker, panel, !isShown(panel));
    });

    listen("focusout", function (e) {
        // No relatedTarget is focus leaving the window, or a press on something that takes none: not a Tab.
        if (opened.size && e.relatedTarget instanceof Node) {
            closeUnless(e.relatedTarget);
        }
    }, true);

    // `toggle` does not bubble; it is caught on the way down.
    listen("toggle", function (e) {
        const panel = e.target;
        if (!(panel instanceof HTMLElement) || !panel.id || !panel.hasAttribute("popover")) {
            return;
        }
        const open = (e as ToggleEvent).newState === "open";
        if (!open) {
            opened.delete(panel);
        }
        markAll(doc, panel, isShown(panel));
        const invokers = doc.querySelectorAll("[aria-expanded]");
        for (let i = 0; i < invokers.length; i++) {
            const el = invokers[i];
            if (el.getAttribute("popovertarget") === panel.id || el.getAttribute("commandfor") === panel.id
                || el.getAttribute(TOGGLE) === panel.id) {
                // Held against the render from the first time it is written: the popover is the browser's.
                own(el, "aria-expanded");
                el.setAttribute("aria-expanded", open ? "true" : "false");
            }
        }
    }, true);

    // The same task as the open or the close, which `toggle` is not. It is cancelable, so what it wrote is
    // looked at again once the popover has opened, or has not.
    listen("beforetoggle", function (e) {
        const panel = e.target;
        if (!(panel instanceof HTMLElement) || !panel.hasAttribute("popover")) {
            return;
        }
        markAll(doc, panel, (e as ToggleEvent).newState === "open");
        setTimeout(function () { markAll(doc, panel, isShown(panel)); }, 0);
    }, true);

    // One that was open before the hooks arrived: a render opened it with the page.
    const marked = doc.querySelectorAll("[" + MARK + "]");
    for (let i = 0; i < marked.length; i++) {
        const name = marked[i].getAttribute(MARK);
        mark(marked[i], isShown(name ? doc.getElementById(name) : marked[i]));
    }
}
