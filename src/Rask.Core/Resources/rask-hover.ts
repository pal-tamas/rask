// Popovers the POINTER opens: a tooltip (data-rask-tooltip) and a hover-opening panel (data-rask-hover).
//
// Why script at all: CSS can show a child on :hover, but it cannot put it in the top layer — and a bubble
// that is not in the top layer is clipped by the first overflow:hidden card around it and sits under every
// dialog. Only showPopover() promotes it, and nothing but a click on a <button popovertarget> calls that for
// free. So an element names its popover by id, and the runtime shows and hides it on the pointer and the
// keyboard, with no delay — measured on Flux UI's live tooltip and `flux:dropdown hover`, both of which open
// in the pointerenter's own task. The tooltip closes in the pointerleave's.
//
// Both are keyed on the ROOT that wraps the trigger and its popover, so "over the root" means over either:
// the popover is the root's descendant in the DOM even though it paints in the top layer. The pixels between
// the two belong to neither.
//
// A hover PANEL does not close in the pointerleave. Measured on Flux's popover page (2026-10-12): once the
// pointer has been over the trigger or the panel, Flux looks at a pointer move at most once in 100 ms and
// closes the panel when the move it looks at is over neither. So a pointer that rested and then jumps into
// the gap closes it at once; one that is MOVING crosses a 10px gap unseen and arrives (a move 83 ms after
// the last one looked at was passed over, one 113 ms after was not). Nothing looks again on its own: a
// pointer that stops outside after a move that was passed over leaves the panel open until it next moves.
// The move that brought the pointer in is not one of those looked at, and coming back over the trigger or
// the panel does not start the 100 ms again.
//
// Delegated: pointerover / pointerout on the document, with the relatedTarget boundary test that turns them
// into enter / leave (the pair that does not bubble). Nothing is attached per element, so a page with no
// such root pays two closest() calls per pointer crossing and nothing else. A touch never hovers.

import {disown, isShown, listen, named, near, own, page, setShown} from "./rask-owned.js";

const TOOLTIP = "data-rask-tooltip";
const HOVER = "data-rask-hover";

// A tooltip that was dismissed on purpose — by a press on its trigger or by Escape — stays away until the
// pointer has left and come back. Without it the bubble would reopen under the finger that just clicked.
const dismissed = new WeakSet<Element>();
const showing = new Set<Element>();

function focusVisibleIn(root: Element): boolean {
    const active = page!.activeElement;
    try {
        return !!active && active !== root && root.contains(active) && active.matches(":focus-visible");
    } catch (e) {
        return false; // an engine without :focus-visible
    }
}

// The interactive variant says which element has the state: the one inside the root carrying aria-expanded.
function mirror(root: Element, open: boolean): void {
    const trigger = root.querySelector("[aria-expanded]");
    if (!trigger) {
        return;
    }
    if (open) own(trigger, "aria-expanded"); else disown(trigger, "aria-expanded");
    trigger.setAttribute("aria-expanded", open ? "true" : "false");
}

function tip(root: Element, open: boolean): void {
    const bubble = named(root, TOOLTIP);
    if (!bubble || isShown(bubble) === open) {
        return;
    }
    setShown(bubble, open);
    if (open) showing.add(root); else showing.delete(root);
    mirror(root, open);
}

// Leaving `root` for somewhere outside it — not a move between two of its own descendants.
function crosses(root: Element, e: Event): boolean {
    const other = (e as PointerEvent).relatedTarget;
    return !(other instanceof Node && root.contains(other));
}

// A panel the pointer opens takes no focus: on Flux UI's rail the menu of a group opens under the pointer and
// document.activeElement stays where it was (measured on its sidebar demo, 2026-10-09). A popover carrying
// `autofocus` — a menu does, so that a press or the keyboard hands it the arrow keys — would take it as it
// shows, so for the length of that one call it does not carry it.
function showUnfocused(panel: HTMLElement | null): void {
    const takes = !!panel && panel.hasAttribute("autofocus");
    if (takes) {
        panel!.removeAttribute("autofocus");
    }
    setShown(panel, true);
    if (takes) {
        panel!.setAttribute("autofocus", "");
    }
}

// The hover roots the pointer has been over and not yet been seen to leave, each with when a move was last
// looked at for it and the move that brought the pointer in — which is the one move not looked at.
interface Watch { looked: number; x: number; y: number; entered: boolean; }
const LOOK_EVERY = 100;
const watched = new Map<Element, Watch>();

function watch(root: Element, e: PointerEvent): void {
    if (!watched.has(root)) {
        watched.set(root, {looked: -LOOK_EVERY, x: e.clientX, y: e.clientY, entered: true});
    }
}

function look(e: PointerEvent): void {
    for (const [root, at] of Array.from(watched)) {
        const entering = at.entered && at.x === e.clientX && at.y === e.clientY;
        at.entered = false;
        if (entering || e.timeStamp - at.looked < LOOK_EVERY) {
            continue;
        }
        at.looked = e.timeStamp;
        if (!(e.target instanceof Node && root.contains(e.target))) {
            watched.delete(root);
            if (hoverable(root)) {
                setShown(named(root, HOVER), false);
            }
        }
    }
}

function hoverable(root: Element): boolean {
    const condition = root.getAttribute("data-rask-hover-if");
    try {
        return !condition || root.matches(condition);
    } catch (e) {
        return false; // not a selector
    }
}

if (page) {
    listen("pointerover", function (e) {
        if ((e as PointerEvent).pointerType === "touch") {
            return;
        }
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip && crosses(tooltip, e) && !dismissed.has(tooltip)) {
            tip(tooltip, true);
        }
        const hover = near(e.target, "[" + HOVER + "]");
        if (hover && crosses(hover, e) && hoverable(hover)) {
            showUnfocused(named(hover, HOVER));
            watch(hover, e as PointerEvent);
        }
    }, {capture: true, passive: true});

    listen("pointermove", function (e) {
        if (watched.size) {
            look(e as PointerEvent);
        }
    }, {capture: true, passive: true});

    listen("pointerout", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip && crosses(tooltip, e)) {
            dismissed.delete(tooltip);
            // Keyboard focus keeps it: a reader who tabbed here and nudged the mouse still needs the label.
            if (!focusVisibleIn(tooltip)) {
                tip(tooltip, false);
            }
        }
    }, {capture: true, passive: true});

    // Keyboard focus shows it, a click's focus does not: :focus-visible is the browser's own answer to
    // "did this arrive by keyboard".
    listen("focusin", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip && !dismissed.has(tooltip) && focusVisibleIn(tooltip)) {
            tip(tooltip, true);
        }
    }, true);

    // Focus that goes to NOTHING — blur(), the window losing it — leaves an interactive tooltip (the one whose
    // trigger carries aria-expanded) open: Flux UI's stays until a press outside, so that what is in it can
    // still be reached. A plain one closes, and so does either when focus moves on to another element.
    listen("focusout", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip && crosses(tooltip, e) && !tooltip.matches(":hover")
            && ((e as FocusEvent).relatedTarget instanceof Node || !tooltip.querySelector("[aria-expanded]"))) {
            tip(tooltip, false);
        }
    }, true);

    listen("pointerdown", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        // A press anywhere else closes whatever is still showing.
        for (const other of Array.from(showing)) {
            if (other !== tooltip) {
                tip(other, false);
            }
        }
        // A press inside the bubble itself (a link in an interactive tooltip) is using it, not dismissing it.
        if (tooltip && !near(e.target, "[popover]")) {
            dismissed.add(tooltip);
            tip(tooltip, false);
        }
    }, {capture: true, passive: true});

    listen("keydown", function (e) {
        if (e.key !== "Escape" || !showing.size) {
            return;
        }
        for (const tooltip of Array.from(showing)) {
            dismissed.add(tooltip);
            tip(tooltip, false);
        }
    }, true);

    // A hover-opened panel is already open when its own button is pressed, and the button's popovertarget
    // would toggle it shut under the pointer that is still on it. So that press is not a toggle: the panel
    // stays. From the keyboard nothing has opened it, and Enter opens it the platform's way.
    listen("click", function (e) {
        const invoker = near(e.target, "[popovertarget]");
        const hover = invoker ? invoker.closest("[" + HOVER + "]") : null;
        if (!invoker || !hover || !hoverable(hover)) {
            return;
        }
        const panel = named(hover, HOVER);
        if (panel && panel.id === invoker.getAttribute("popovertarget") && isShown(panel) && hover.matches(":hover")) {
            e.preventDefault();
        }
    }, true);
}
