// A plot that answers the pointer, and an element that reports its own size.
//
// POINTER (data-rask-plot). A chart's cursor, tooltip and summary follow the pointer every frame; a round
// trip per move would trail it. So the page renders EVERY row's version of them once, each marked
// `data-rask-plot-row="<index>"`, and says where the rows are; the runtime only moves `data-active`:
//
//   data-rask-plot="0 0.25 0.5 1"    on the root: each row's x, 0 to 1 across the area, in row order
//   data-rask-plot-area              inside it: the element whose box is the plot — the active region
//   data-rask-plot-row="<index>"     anything that belongs to one row: lit while that row is the nearest
//   data-rask-plot-tooltip="<px>"    an absolutely placed box moved beside the active row, that far from it
//
// While the pointer is inside the area (edges included) the root carries `data-active`, `--rask-plot-x` (the
// active row's x) and `--rask-pointer-x` / `--rask-pointer-y` (the pointer), all in px from the root's
// top-left corner; the row nearest in x — the switch is at the midpoint between two rows — has `data-active`
// on each of its elements; and the tooltip has `data-active` and `transform: translate(x, y)` with x snapped
// to the row and y following the pointer, each flipped to the other side when the box would leave the root.
// Outside, the marks come off and everything else is left where it was. Measured on Flux UI's chart.
//
// SIZE (data-rask-measure). A chart is drawn in the units of its own box, which only the browser knows. An
// element carrying data-rask-measure holds ONE bound field (rask-bound.ts) that the runtime keeps at
// "<width> <height>" of its content box, in CSS px, announced when it arrives and whenever it changes — at
// most once a frame, which is how a ResizeObserver delivers. A value the page writes there is not adopted:
// the box is the browser's.

import {announce} from "./rask-bound.js";
import {disown, listen, near, own, page} from "./rask-owned.js";

const PLOT = "[data-rask-plot]";
const MEASURE = "[data-rask-measure]";

/** The index of the value in `xs` nearest to `x`; the earlier one at an exact midpoint. -1 for none. */
export function nearestRow(xs: number[], x: number): number {
    let best = -1;
    for (let i = 0; i < xs.length; i++) {
        if (best < 0 || Math.abs(xs[i] - x) < Math.abs(xs[best] - x)) {
            best = i;
        }
    }
    return best;
}

/** Where a box of `size` goes beside `at`: `gap` after it, or `gap` before it when it would pass `limit`. */
export function beside(at: number, size: number, gap: number, limit: number): number {
    return at + gap + size > limit ? at - gap - size : at + gap;
}

function mark(el: Element, on: boolean): void {
    if (on === el.hasAttribute("data-active")) {
        return;
    }
    if (on) {
        own(el, "data-active");
        el.setAttribute("data-active", "");
    } else {
        el.removeAttribute("data-active");
        disown(el, "data-active");
    }
}

function light(root: Element, row: number): void {
    const rows = root.querySelectorAll("[data-rask-plot-row]");
    for (let i = 0; i < rows.length; i++) {
        mark(rows[i], rows[i].getAttribute("data-rask-plot-row") === "" + row);
    }
}

/** A content box as the bound field carries it: "<width> <height>" in CSS px, to a hundredth. */
export function sizeText(width: number, height: number): string {
    return Math.round(width * 100) / 100 + " " + Math.round(height * 100) / 100;
}

if (page) {
    const doc = page;
    let over: HTMLElement | null = null;

    const leave = function (): void {
        if (!over) {
            return;
        }
        mark(over, false);
        light(over, -1);
        const tooltip = over.querySelector("[data-rask-plot-tooltip]");
        if (tooltip) mark(tooltip, false);
        over = null;
    };

    listen("pointermove", function (e) {
        const root = near(e.target, PLOT) || over;
        const area = root ? root.querySelector("[data-rask-plot-area]") : null;
        if (!root || !area) {
            leave();
            return;
        }
        const box = area.getBoundingClientRect();
        if (e.clientX < box.left || e.clientX > box.right || e.clientY < box.top || e.clientY > box.bottom
            || box.width <= 0) {
            leave();
            return;
        }
        if (over !== root) {
            leave();
            over = root;
        }
        const frame = root.getBoundingClientRect();
        const xs = (root.getAttribute("data-rask-plot") || "").split(" ").filter(Boolean).map(Number);
        const row = nearestRow(xs, (e.clientX - box.left) / box.width);
        const x = row < 0 ? e.clientX - frame.left : box.left - frame.left + xs[row] * box.width;
        const y = e.clientY - frame.top;
        own(root, "style");
        root.style.setProperty("--rask-plot-x", x + "px");
        root.style.setProperty("--rask-pointer-x", e.clientX - frame.left + "px");
        root.style.setProperty("--rask-pointer-y", y + "px");
        mark(root, true);
        light(root, row);
        const tooltip = root.querySelector<HTMLElement>("[data-rask-plot-tooltip]");
        if (tooltip) {
            // Measured before it is lit, as there: a row's text of another width is a frame late.
            const gap = Number(tooltip.getAttribute("data-rask-plot-tooltip")) || 0;
            own(tooltip, "style");
            tooltip.style.transform = "translate(" + beside(x, tooltip.offsetWidth, gap, frame.width) + "px, "
                + beside(y, tooltip.offsetHeight, gap, frame.height) + "px)";
            mark(tooltip, true);
        }
    }, {capture: true, passive: true});

    // The pointer left the window, or its last position is gone with the element it was over.
    listen("pointerout", function (e) {
        if (!(e as PointerEvent).relatedTarget) leave();
    }, {capture: true, passive: true});

    if (typeof ResizeObserver === "function" && typeof MutationObserver === "function") {
        const sizes = new ResizeObserver(function (entries) {
            for (const entry of entries) {
                if (entry.target.isConnected) {
                    announce(entry.target, sizeText(entry.contentRect.width, entry.contentRect.height));
                }
            }
        });
        const watch = function (el: Element): void {
            sizes.observe(el);
        };
        new MutationObserver(function (records) {
            for (const record of records) {
                record.addedNodes.forEach(function (n) {
                    if (n instanceof Element) {
                        if (n.matches(MEASURE)) watch(n);
                        n.querySelectorAll(MEASURE).forEach(watch);
                    }
                });
            }
        }).observe(doc.documentElement, {subtree: true, childList: true});
        doc.querySelectorAll(MEASURE).forEach(watch);
    }
}
