// A plot that answers the pointer, and an element that reports its own size.
//
// POINTER (data-rask-plot). A chart's cursor, tooltip and summary follow the pointer every frame; a round
// trip per move would trail it. So the page says where its rows are and what each one reads, once, and the
// runtime does the rest in the browser:
//
//   data-rask-plot="0 0.25 0.5 1"    on the root: each row's place, 0 to 1 across the area, in row order
//   data-rask-plot-area              inside it: the element whose box is the plot — the active region. It may
//                                    carry the rows' places itself (data-rask-plot-area="0 0.25 0.5 1", the
//                                    root's attribute left bare) when only the drawing knows them, and
//                                    data-rask-plot-axis="y" when the rows run DOWN the area
//   data-rask-plot-row="<index>"     anything that belongs to one row: lit while that row is the active one
//   data-rask-plot-text              an element that reads differently for each row: the attribute holds its
//                                    text for every row, one per LINE; it shows the active row's, and what
//                                    was rendered while there is none
//   data-rask-plot-tooltip="<px>"    an absolutely placed box moved beside the active row, that far from it
//   data-rask-plot-frame             the box the tooltip is kept inside (the drawing); the root without one
//
// While the pointer is inside the area (edges included) the root carries `data-active`, `--rask-plot-x` (the
// active row's x; `--rask-plot-y` when the rows run down), `--rask-plot-at` (the same row as a fraction of the
// area, 0 to 1) and `--rask-pointer-x` / `--rask-pointer-y` (the pointer), in px from the root's top-left
// corner; the row nearest along the axis — the switch is at the midpoint between two rows — has `data-active`
// on each of its elements; and the tooltip has `data-active` and `transform: translate(x, y)`, snapped to the
// row along the axis and following the pointer across it, each flipped to the other side when the box would
// pass the frame's right or bottom edge. Outside, the marks come off, each text goes back to what was rendered, and everything else
// is left where it was.
//
// A root with NO area is a plot of shapes (a pie): the active row is the `data-rask-plot-row` element under
// the pointer, every other row's elements carry `data-inactive` meanwhile, and the tooltip follows the
// pointer both ways. Measured on Flux UI's chart.
//
// SIZE (data-rask-measure). A chart is drawn in the units of its own box, which only the browser knows. An
// element carrying data-rask-measure holds ONE bound field (rask-bound.ts) that the runtime keeps at
// "<width> <height>" of its content box, in CSS px: announced when the element arrives, and after that once
// its box has stopped changing for a tenth of a second — a redraw per frame of a window being dragged is a
// round trip per frame per chart. An element with no box (hidden) announces nothing: it keeps the size it was
// last drawn at, as Flux's chart keeps its drawing. Nor does a box within half a pixel of what the field
// says: a chart rendered for the size it turns out to have is not asked to draw itself again over the
// hundredth of a pixel a grid rounds its columns apart. A value the page writes there is not adopted: the
// box is the browser's, and a page that writes another one is told the box again — which is how a page that
// was prerendered, measured before any host was there to hear it, gets its sizes once the host takes over.

import {announce, bind, boundOf} from "./rask-bound.js";
import {disown, listen, near, own, page} from "./rask-owned.js";

const PLOT = "[data-rask-plot]";
const MEASURE = "[data-rask-measure]";
const SETTLED = 100;

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

/** The line of `lines` (one row's text per line) that row `row` reads; nothing for a row it has no line for. */
export function lineOf(lines: string, row: number): string {
    return row < 0 ? "" : lines.split("\n")[row] || "";
}

function mark(el: Element, on: boolean, name: string = "data-active"): void {
    if (on === el.hasAttribute(name)) {
        return;
    }
    if (on) {
        own(el, name);
        el.setAttribute(name, "");
    } else {
        el.removeAttribute(name);
        disown(el, name);
    }
}

// `dim`: a plot of shapes also marks every row that is NOT the active one, while there is one.
function light(root: Element, row: number, dim: boolean): void {
    const rows = root.querySelectorAll("[data-rask-plot-row]");
    for (let i = 0; i < rows.length; i++) {
        const on = rows[i].getAttribute("data-rask-plot-row") === "" + row;
        mark(rows[i], on);
        mark(rows[i], dim && row >= 0 && !on, "data-inactive");
    }
}

// What each text read before the pointer arrived, and what was written into it since: a text that no longer
// says what was written was rendered again meanwhile, and THAT is what it goes back to.
const rest = new WeakMap<Element, string>();
const wrote = new WeakMap<Element, string>();

/** Every text of the plot shows row `row`'s line; -1 puts back what was rendered. */
function write(root: Element, row: number): void {
    const texts = root.querySelectorAll("[data-rask-plot-text]");
    for (let i = 0; i < texts.length; i++) {
        const el = texts[i];
        const shown = el.textContent || "";
        if (!rest.has(el) || shown !== wrote.get(el)) {
            rest.set(el, shown);
        }
        const text = row < 0 ? rest.get(el) || "" : lineOf(el.getAttribute("data-rask-plot-text") || "", row);
        if (shown !== text) {
            el.textContent = text;
        }
        if (row < 0) {
            rest.delete(el);
            wrote.delete(el);
        } else {
            wrote.set(el, text);
        }
    }
}

const places = new WeakMap<Element, {text: string; at: number[]}>();

/** The rows' places along the area: the root's list, or the area's own when the root's is bare. */
function placed(root: Element, area: Element): number[] {
    const text = root.getAttribute("data-rask-plot") || area.getAttribute("data-rask-plot-area") || "";
    let known = places.get(root);
    if (!known || known.text !== text) {
        places.set(root, known = {text, at: text.split(" ").filter(Boolean).map(Number)});
    }
    return known.at;
}

/** A content box as the bound field carries it: "<width> <height>" in CSS px, to a hundredth. */
export function sizeText(width: number, height: number): string {
    return Math.round(width * 100) / 100 + " " + Math.round(height * 100) / 100;
}

/** Whether `size` ("<width> <height>") already says a box of `width` by `height`, to within half a pixel. */
export function sameBox(size: string, width: number, height: number): boolean {
    const said = size.split(" ").map(Number);
    return said.length === 2 && Math.abs(said[0] - width) < 0.5 && Math.abs(said[1] - height) < 0.5;
}

if (page) {
    const doc = page;
    let over: HTMLElement | null = null;
    let lit = -1;

    const leave = function (): void {
        if (!over) {
            return;
        }
        mark(over, false);
        light(over, -1, false);
        write(over, -1);
        const tooltip = over.querySelector("[data-rask-plot-tooltip]");
        if (tooltip) mark(tooltip, false);
        over = null;
        lit = -1;
    };

    listen("pointermove", function (e) {
        const root = near(e.target, PLOT) || over;
        if (!root) {
            return;
        }
        const area = root.querySelector("[data-rask-plot-area]");
        const frame = root.getBoundingClientRect();
        const px = e.clientX - frame.left;
        const py = e.clientY - frame.top;
        const down = !!area && area.getAttribute("data-rask-plot-axis") === "y";
        // Where the tooltip is anchored — the pointer, moved onto the active row along the axis.
        let x = px;
        let y = py;
        let row: number;
        let at = 0;
        if (area) {
            const box = area.getBoundingClientRect();
            if (e.clientX < box.left || e.clientX > box.right || e.clientY < box.top || e.clientY > box.bottom
                || box.width <= 0 || box.height <= 0) {
                leave();
                return;
            }
            const rows = placed(root, area);
            row = nearestRow(rows, down ? (e.clientY - box.top) / box.height : (e.clientX - box.left) / box.width);
            if (row >= 0) {
                at = rows[row];
                if (down) {
                    y = box.top - frame.top + at * box.height;
                } else {
                    x = box.left - frame.left + at * box.width;
                }
            }
        } else {
            const shape = near(e.target, "[data-rask-plot-row]");
            if (!shape || !root.contains(shape)) {
                leave();
                return;
            }
            row = Number(shape.getAttribute("data-rask-plot-row"));
        }
        if (over !== root) {
            leave();
            over = root;
        }
        own(root, "style");
        root.style.setProperty(down ? "--rask-plot-y" : "--rask-plot-x", (down ? y : x) + "px");
        root.style.setProperty("--rask-plot-at", "" + at);
        root.style.setProperty("--rask-pointer-x", px + "px");
        root.style.setProperty("--rask-pointer-y", py + "px");
        mark(root, true);
        const tooltip = root.querySelector<HTMLElement>("[data-rask-plot-tooltip]");
        // Measured before the row's own text is in it, as there: a text of another width is a move late.
        const size = tooltip ? tooltip.getBoundingClientRect() : null;
        if (row !== lit) {
            lit = row;
            light(root, row, !area);
            write(root, row);
        }
        if (tooltip && size) {
            // From wherever the box rests: its own place in the root is taken back out of the move.
            const gap = Number(tooltip.getAttribute("data-rask-plot-tooltip")) || 0;
            const within = root.querySelector("[data-rask-plot-frame]");
            const edge = within ? within.getBoundingClientRect() : frame;
            own(tooltip, "style");
            tooltip.style.transform = "translate("
                + (beside(x, size.width, gap, edge.right - frame.left) - tooltip.offsetLeft) + "px, "
                + (beside(y, size.height, gap, edge.bottom - frame.top) - tooltip.offsetTop) + "px)";
            mark(tooltip, true);
        }
    }, {capture: true, passive: true});

    // The pointer left the window, or its last position is gone with the element it was over.
    listen("pointerout", function (e) {
        if (!(e as PointerEvent).relatedTarget) leave();
    }, {capture: true, passive: true});

    if (typeof ResizeObserver === "function" && typeof MutationObserver === "function") {
        const told = new WeakSet<Element>();
        const boxes = new WeakMap<Element, DOMRectReadOnly>();
        const changing = new Map<Element, string>();
        let timer: ReturnType<typeof setTimeout> | null = null;
        const settle = function (): void {
            timer = null;
            changing.forEach(function (size, el) {
                if (el.isConnected) announce(el, size);
            });
            changing.clear();
        };
        const sizes = new ResizeObserver(function (entries) {
            let replaced = false;
            for (const entry of entries) {
                const el = entry.target;
                const box = entry.contentRect;
                if (!el.isConnected) {
                    // A render put another element where this one was: that one is the measured box now.
                    sizes.unobserve(el);
                    replaced = true;
                    continue;
                }
                if (box.width <= 0 || box.height <= 0) {
                    continue;
                }
                boxes.set(el, box);
                const field = boundOf(el);
                if (!field || sameBox(field.value, box.width, box.height)) {
                    told.add(el);
                    changing.delete(el);
                    continue;
                }
                const size = sizeText(box.width, box.height);
                if (told.has(el)) {
                    changing.set(el, size);
                } else {
                    told.add(el);
                    announce(el, size);
                }
            }
            if (changing.size > 0) {
                if (timer !== null) clearTimeout(timer);
                timer = setTimeout(settle, SETTLED);
            }
            if (replaced) {
                doc.querySelectorAll(MEASURE).forEach(watch);
            }
        });
        const watch = function (el: Element): void {
            sizes.observe(el);
        };
        // The field says what the box measured last; until it has been measured, whatever it says. A render
        // that writes a size the box is not (a host taking a prerendered page over) is answered with the box.
        bind(MEASURE, function (el) {
            const box = boxes.get(el);
            const field = boundOf(el);
            return box ? sizeText(box.width, box.height) : field ? field.value : "";
        }, function (el, rendered) {
            const box = boxes.get(el);
            if (box && !sameBox(rendered, box.width, box.height)) {
                announce(el, sizeText(box.width, box.height));
            }
        });
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
