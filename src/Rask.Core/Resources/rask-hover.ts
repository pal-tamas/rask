// Popovers the POINTER opens: a tooltip (data-rask-tooltip) and a hover-opening panel (data-rask-hover).
//
// Why script at all: CSS can show a child on :hover, but it cannot put it in the top layer — and a bubble
// that is not in the top layer is clipped by the first overflow:hidden card around it and sits under every
// dialog. Only showPopover() promotes it, and nothing but a click on a <button popovertarget> calls that for
// free. So an element names its popover by id, and the runtime shows and hides it on the pointer and the
// keyboard, with no delay — measured on Flux UI's live tooltip and `flux:dropdown hover`, both of which open
// in the pointerenter's own task.
//
// Both are keyed on the ROOT that wraps the trigger and its popover, so "over the root" means over either:
// the popover is the root's descendant in the DOM even though it paints in the top layer. The pixels between
// the two belong to neither.
//
// Neither closes in the pointerleave. Measured on Flux's popover page (2026-10-12) and its tooltip page
// (2026-11-02): once the pointer has been over the trigger or the popover, Flux looks at a pointer move at most
// once in 100 ms and closes the popover when the move it looks at is over neither. So a pointer that rested and
// then jumps into the gap — or anywhere off the trigger — closes it at once; one that is MOVING crosses the gap
// unseen and arrives, and may rest on a tooltip as on a panel (a move 83 ms after the last one looked at was
// passed over, one 113 ms after was not). Nothing looks again on its own: a pointer that stops outside after a
// move that was passed over leaves it open until it next moves. The move that brought the pointer in is not
// one of those looked at, and coming back over the trigger or the popover does not start the 100 ms again.
//
// Delegated: pointerover / pointerout on the document, with the relatedTarget boundary test that turns them
// into enter / leave (the pair that does not bubble). Nothing is attached per element, so a page with no
// such root pays two closest() calls per pointer crossing and nothing else. A touch opens no hover panel.
//
// A touch DOES show a tooltip. Measured on Flux UI's tooltip page on a phone (2026-10-27): the finger's
// pointerenter shows it and its pointerleave, a moment later, hides nothing. Lifting the finger is a press
// (the mousedown the browser makes of the tap), which hides a tooltip shown before the frame it lands in —
// so an instant tap leaves it shown and a finger that rested hides it again. A frame is shorter than a person's
// tap (a finger is down 50–150 ms), and a busy page or browser lets one pass under the quickest: here a TAP —
// the finger lifted within TAP_MS of landing — leaves it shown (owner, 2026-11-11: a tooltip opens on a tap),
// and only a finger that rested longer hides it as it lifts. A second tap hides it, as does a
// tap elsewhere. And once a finger has shown one, Flux shows no tooltip of the page to a finger again — not
// even another one — until a mouse moves.

import {disown, isReplayed, isShown, listen, named, near, own, page, setShown} from "./rask-owned.js";

const TOOLTIP = "data-rask-tooltip";
const HOVER = "data-rask-hover";

// A tooltip that was dismissed on purpose — by a press on its trigger or by Escape — stays away until the
// pointer has left and come back. Without it the bubble would reopen under the finger that just clicked.
const dismissed = new WeakSet<Element>();
const showing = new Set<Element>();

// The tooltip a finger showed, until a mouse moves; and how long the finger that last landed was down (to its
// lift; while it is still down, Infinity).
let touched: Element | null = null;
let fingerAt = 0;
let fingerHeld = Infinity;
const TAP_MS = 300;

// The roots whose tooltip keyboard focus showed, until focus leaves them or a press lands in them: only such a
// tooltip outlives the pointer leaving. One the pointer showed over a trigger that merely HAS focus — handed back
// by a menu that closed — goes with the pointer.
const keyed = new WeakSet<Element>();

// What the reader last did: a key or a press. Read only for focus that comes from nowhere (a focusin with no
// relatedTarget), to tell the first Tab into a page — or a shortcut's own focus() — from focus a script hands back
// after a press.
let lastInput: "key" | "press" = "press";

/**
 * Focus HANDED BACK rather than navigated to: it comes out of a popover (or a dialog) that does not hold the new
 * focus — a menu closed by a pick or by Escape, where the browser returns focus to the button that opened it — or
 * it comes from nowhere after a press: rask-menu-keys.ts handing focus back after a click outside. Chrome and WebKit count either as :focus-visible (a script's focus after a key, or after an
 * `autofocus` menu had it), so :focus-visible alone cannot tell it from a Tab. A Tab, or a move between two controls
 * of one popover or of the page (a toolbar's arrow keys), is navigation, and shows it.
 *
 * Measured on Flux UI's editor (2026-11-10, `flux tooltip-focus-return`): its link button wears a tooltip and opens
 * a dropdown; closed by a click inside, a click outside or Escape — after a mouse open and after Enter alike — focus
 * is back on the button, matching :focus-visible after Escape, and the tooltip does not show. A Tab onto it does.
 */
export function handedBack(to: Node, from: Pick<Element, "closest"> | null, last: string): boolean {
    if (!from) {
        return last === "press";
    }
    const panel = from.closest("[popover], dialog");
    return !!panel && !panel.contains(to);
}

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
// The panels the pointer opened, until they close: only such a panel is kept by a press on its button.
const hovered = new WeakSet<Element>();

function showUnfocused(panel: HTMLElement | null): void {
    if (panel && !isShown(panel)) {
        hovered.add(panel);
    }
    const takes = !!panel && panel.hasAttribute("autofocus");
    if (takes) {
        panel!.removeAttribute("autofocus");
    }
    setShown(panel, true);
    if (takes) {
        panel!.setAttribute("autofocus", "");
    }
}

// The roots — a tooltip's or a hover panel's — the pointer has been over and not yet been seen to leave, each with when a move was last
// looked at for it and the move that brought the pointer in — which is the one move not looked at.
interface Watch { looked: number; x: number; y: number; entered: boolean; }
const LOOK_EVERY = 100;
const watched = new Map<Element, Watch>();

/**
 * The panels the pointer walked away from, until their `toggle` is heard. Such a close hands focus to nobody:
 * on Flux UI a hover card the pointer left leaves `document.activeElement` where it was, on the page itself
 * when nothing had it (rask-menu-keys.ts gives a menu closed any other way back to its button).
 */
export const walkedFrom = new WeakSet<Element>();

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
            leave(root);
        }
    }
}

// The pointer seen over neither the root's trigger nor its popover.
function leave(root: Element): void {
    watched.delete(root);
    if (root.hasAttribute(TOOLTIP)) {
        leaveTooltip(root);
        return;
    }
    const panel = named(root, HOVER);
    if (panel && hoverable(root) && isShown(panel)) {
        walkedFrom.add(panel);
        setShown(panel, false);
    }
}

// A leave the hooks missed while their bundle was on its way (isReplayed): the moves after it were not kept, so no
// move is left to look at. Where the pointer is NOW says it — a pointer that left before the hooks arrived and
// never came back leaves nothing open.
function leftWhileAway(e: Event): void {
    for (const root of Array.from(watched.keys())) {
        if (e.target instanceof Node && root.contains(e.target) && crosses(root, e) && !root.matches(":hover")) {
            leave(root);
        }
    }
}

// The pointer seen over neither the trigger nor the tooltip. Keyboard focus keeps it: a reader who tabbed here
// and nudged the mouse still needs the label.
function leaveTooltip(root: Element): void {
    if (!keyed.has(root) || !focusVisibleIn(root)) {
        tip(root, false);
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
        const touch = (e as PointerEvent).pointerType === "touch";
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip && crosses(tooltip, e) && !dismissed.has(tooltip) && !(touch && touched)) {
            tip(tooltip, true);
            if (touch) {
                touched = tooltip;
            } else {
                watch(tooltip, e as PointerEvent);
            }
        }
        if (touch) {
            return;
        }
        const hover = near(e.target, "[" + HOVER + "]");
        if (hover && crosses(hover, e) && hoverable(hover)) {
            showUnfocused(named(hover, HOVER));
            watch(hover, e as PointerEvent);
        }
    }, {capture: true, passive: true});

    listen("pointermove", function (e) {
        if ((e as PointerEvent).pointerType !== "touch") {
            touched = null;
        }
        if (watched.size) {
            look(e as PointerEvent);
        }
    }, {capture: true, passive: true});

    listen("pointerout", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        // A lifted finger leaves nothing. A mouse that left may come back: a press or Escape no longer keeps the
        // tooltip away. Whether it closes is the next move's to say (look).
        if (tooltip && crosses(tooltip, e) && (e as PointerEvent).pointerType !== "touch") {
            dismissed.delete(tooltip);
        }
        if (isReplayed()) {
            leftWhileAway(e);
        }
    }, {capture: true, passive: true});

    // Keyboard focus shows it, a click's focus does not: :focus-visible is the browser's own answer to
    // "did this arrive by keyboard". Focus handed back to the trigger is not keyboard navigation, whatever
    // :focus-visible says of it (handedBack).
    listen("focusin", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        const from = (e as FocusEvent).relatedTarget;
        if (tooltip && !dismissed.has(tooltip) && focusVisibleIn(tooltip)
            && !handedBack(e.target as Node, from instanceof Element ? from : null, lastInput)) {
            keyed.add(tooltip);
            tip(tooltip, true);
        }
    }, true);

    // Focus that goes to NOTHING — blur(), the window losing it — leaves an interactive tooltip (the one whose
    // trigger carries aria-expanded) open: Flux UI's stays until a press outside, so that what is in it can
    // still be reached. A plain one closes, and so does either when focus moves on to another element.
    listen("focusout", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip && crosses(tooltip, e)) {
            keyed.delete(tooltip);
        }
        if (tooltip && crosses(tooltip, e) && !tooltip.matches(":hover")
            && ((e as FocusEvent).relatedTarget instanceof Node || !tooltip.querySelector("[aria-expanded]"))) {
            tip(tooltip, false);
        }
    }, true);

    listen("pointerdown", function (e) {
        lastInput = "press";
        if ((e as PointerEvent).pointerType === "touch") {
            fingerAt = e.timeStamp;
            fingerHeld = Infinity;
        }
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip) {
            keyed.delete(tooltip);
        }
        // A press anywhere else closes whatever is still showing.
        for (const other of Array.from(showing)) {
            if (other !== tooltip) {
                tip(other, false);
            }
        }
        // A press inside the bubble itself (a link in an interactive tooltip) is using it, not dismissing it.
        // A finger's press is not yet one: it is the mousedown it ends in (below).
        if (tooltip && !near(e.target, "[popover]") && (e as PointerEvent).pointerType !== "touch") {
            dismissed.add(tooltip);
            tip(tooltip, false);
        }
    }, {capture: true, passive: true});

    listen("pointerup", function (e) {
        if ((e as PointerEvent).pointerType === "touch") {
            fingerHeld = e.timeStamp - fingerAt;
        }
    }, {capture: true, passive: true});

    // The press a tap ends in, on the tooltip a finger showed: it hides it, unless that finger only tapped.
    listen("mousedown", function (e) {
        const tooltip = near(e.target, "[" + TOOLTIP + "]");
        if (tooltip && tooltip === touched && fingerHeld >= TAP_MS && !near(e.target, "[popover]")) {
            tip(tooltip, false);
        }
    }, {capture: true, passive: true});

    listen("keydown", function (e) {
        lastInput = "key";
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
    // stays. From the keyboard nothing has opened it, and Enter opens it the platform's way. Nor has a finger
    // (a touch never hovers): on a phone each tap on the button toggles it, and so does Enter after a tap
    // opened it, as on Flux UI's popover page (2026-10-27).
    listen("toggle", function (e) {
        if ((e as ToggleEvent).newState === "closed" && e.target instanceof Element) {
            hovered.delete(e.target);
        }
    }, true);

    listen("click", function (e) {
        if ((e as PointerEvent).pointerType === "touch") {
            return;
        }
        const invoker = near(e.target, "[popovertarget]");
        const hover = invoker ? invoker.closest("[" + HOVER + "]") : null;
        if (!invoker || !hover || !hoverable(hover)) {
            return;
        }
        const panel = named(hover, HOVER);
        if (panel && panel.id === invoker.getAttribute("popovertarget") && isShown(panel) && hovered.has(panel) && hover.matches(":hover")) {
            e.preventDefault();
        }
    }, true);
}
