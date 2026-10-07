// A popover toggled from something that is not a <button popovertarget>, and aria-expanded kept true to it.
//
//   * data-rask-toggle="<popover id>" on any element: a click on it, and Enter or Space while it or something
//     inside it that is not a control of its own has focus, toggle that popover. Measured on Flux UI's toggleable tooltip: hovering does nothing; a click opens it
//     and the next closes it; Enter and Space do the same; Escape, a press outside both, and Tab away close
//     it; a press inside the bubble does not. A popover="auto" gets the last three from the platform. A
//     popover="manual" — what Flux's is — gets them here, for as long as this hook is what opened it.
//     Focus and tabindex are the component's: an element that cannot be focused is toggled by the pointer only.
//   * aria-expanded follows the popover: whenever one opens or closes — however it was asked to — every
//     element that invokes it (popovertarget, commandfor, data-rask-toggle) AND already carries aria-expanded
//     is rewritten. The attribute is never added where the render did not put it.

import {isShown, named, near, own, page, setShown} from "./rask-owned.js";

const TOGGLE = "data-rask-toggle";

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

    doc.addEventListener("pointerdown", function (e) {
        const invoker = near(e.target, "[" + TOGGLE + "]");
        const panel = invoker ? named(invoker, TOGGLE) : null;
        shownAtPress = panel && isShown(panel) ? panel : null;
        closeUnless(e.target);
    }, {capture: true, passive: true});

    doc.addEventListener("click", function (e) {
        const invoker = near(e.target, "[" + TOGGLE + "]");
        const panel = invoker ? named(invoker, TOGGLE) : null;
        // A click inside the popover (it may be the invoker's own child) is using it.
        if (!invoker || !panel || (e.target instanceof Node && panel.contains(e.target))) {
            return;
        }
        // detail 0 is a click the keyboard made (Enter on a button): no press came before it.
        const wasShown = (e as MouseEvent).detail === 0 ? isShown(panel) : shownAtPress === panel;
        shownAtPress = null;
        set(invoker, panel, !wasShown);
    });

    doc.addEventListener("keydown", function (e) {
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

    doc.addEventListener("focusout", function (e) {
        // No relatedTarget is focus leaving the window, or a press on something that takes none: not a Tab.
        if (opened.size && e.relatedTarget instanceof Node) {
            closeUnless(e.relatedTarget);
        }
    }, true);

    // `toggle` does not bubble; it is caught on the way down.
    doc.addEventListener("toggle", function (e) {
        const panel = e.target;
        if (!(panel instanceof HTMLElement) || !panel.id || !panel.hasAttribute("popover")) {
            return;
        }
        const open = (e as ToggleEvent).newState === "open";
        if (!open) {
            opened.delete(panel);
        }
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
}
