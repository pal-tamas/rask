// A group of toasts: one pointer over any of it holds them all, and the stack knows its own measurements.
//
// The countdown itself is data-rask-dismiss-after's (rask-dom.ts). Two things about a GROUP it cannot see:
//
//   * data-rask-dismiss-scope on an ancestor: while the pointer is anywhere over the scope, every countdown
//     inside it is held, and they all run on from where they stopped when it leaves. Flux UI's toast group
//     does this (three toasts, hovered for 6.7 s, all three still there; each then ran out its own remainder).
//     Each toast is told with a `rask-hold` / `rask-release` event, which is the whole interface between the
//     two modules.
//   * data-rask-stack on the element whose children are the stacked toasts: each child gets, in its style,
//     --rask-stack-index (0 for the front one — the last child — counting back), --rask-stack-height (its own
//     NATURAL height), --rask-stack-offset (the natural heights of everything in front of it, added up) and
//     --rask-stack-front (the front child's natural height, the same on every child). With those a
//     stylesheet can cut every card to the front one's height while the stack is closed, fan it out on
//     hover, and let a transition carry each toast to its place, which is how Flux's group glides (350 ms);
//     without them CSS cannot know how tall its siblings are.
//     NATURAL means the height the child has when nothing cuts it: a stylesheet that cuts the cards to
//     var(--rask-stack-front) would otherwise be handed its own cut back. So while it measures, the stack
//     carries data-rask-measuring, and the rule that cuts says `:not([data-rask-measuring])`. The mark is
//     gone again, and the cut laid out again, before anything is painted — no transition sees it.
//     Measured again whenever the stack gains or loses a child, and when the window is resized.

import {near, own, page} from "./rask-owned.js";

const SCOPE = "[data-rask-dismiss-scope]";
const STACK = "[data-rask-stack]";

function tell(scope: Element, type: string): void {
    const toasts = scope.querySelectorAll("[data-rask-dismiss-after]");
    for (let i = 0; i < toasts.length; i++) {
        toasts[i].dispatchEvent(new Event(type));
    }
}

function measure(stack: Element): void {
    const children: HTMLElement[] = [];
    for (let child = stack.lastElementChild; child; child = child.previousElementSibling) {
        if (child instanceof HTMLElement && !child.hasAttribute("data-rask-managed")) {
            children.push(child);
        }
    }
    stack.setAttribute("data-rask-measuring", "");
    const heights = children.map(function (child) { return child.offsetHeight; });
    stack.removeAttribute("data-rask-measuring");
    // Laid out once more as it was: what a card glides from is the height it had, not the one it was measured at.
    if (stack instanceof HTMLElement) void stack.offsetHeight;
    let offset = 0;
    children.forEach(function (child, index) {
        // The style attribute is the toast's own render's too; from here on these four are kept through it.
        own(child, "style");
        child.style.setProperty("--rask-stack-index", "" + index);
        child.style.setProperty("--rask-stack-height", heights[index] + "px");
        child.style.setProperty("--rask-stack-offset", offset + "px");
        child.style.setProperty("--rask-stack-front", heights[0] + "px");
        offset += heights[index];
    });
}

if (page) {
    const doc = page;

    doc.addEventListener("pointerover", function (e) {
        const scope = near(e.target, SCOPE);
        const from = (e as PointerEvent).relatedTarget;
        if (scope && !(from instanceof Node && scope.contains(from))) {
            tell(scope, "rask-hold");
        }
    }, {capture: true, passive: true});

    doc.addEventListener("pointerout", function (e) {
        const scope = near(e.target, SCOPE);
        const to = (e as PointerEvent).relatedTarget;
        if (scope && !(to instanceof Node && scope.contains(to))) {
            tell(scope, "rask-release");
        }
    }, {capture: true, passive: true});

    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            const seen: Element[] = [];
            for (const record of records) {
                const stack = record.target instanceof Element && record.target.matches(STACK) ? record.target : null;
                if (stack && seen.indexOf(stack) < 0) {
                    seen.push(stack);
                    measure(stack);
                }
                record.addedNodes.forEach(function (n) {
                    if (n instanceof Element) {
                        if (n.matches(STACK)) measure(n);
                        n.querySelectorAll(STACK).forEach(measure);
                    }
                });
            }
        }).observe(doc.documentElement, {subtree: true, childList: true});
        doc.querySelectorAll(STACK).forEach(measure);
    }

    window.addEventListener("resize", function () {
        doc.querySelectorAll(STACK).forEach(measure);
    }, {passive: true});
}
