// A table that turns its rows into skeleton rows the moment the reader asks it for other ones
// (data-rask-skeleton, written by `Ui.Table.Of(query)`).
//
// A page change, a sort, a filter or a tab took the rows away and brought the new ones a round trip later, and
// in between the table was empty, or shorter, or a spinner: everything under it jumped twice. Here nothing on
// the page moves. At the press, in the browser, the rows that are there keep their places and draw a bar
// instead of what they hold (the kit's sheet draws it on `data-skeleton`), and their row group keeps the
// height it had, so a render that brings fewer or taller placeholder rows meanwhile changes nothing either.
// The headings stay as they are. When the page has answered — the render that answers the press is on the
// page — and the render no longer says its query is loading (`data-rask-skeleton="loading"`), the new rows
// take the skeleton's place in one step.
//
// WHAT ASKS. A press is for the table when it starts at:
//   - a control inside the table's box that the runtime marks while it waits (`data-rask-loading`: a sortable
//     heading, the pager's root) and is not inside a row group — a button in a row is the row's business;
//   - such a control right after the box: a pager placed under the table rather than in it;
//   - a control that names the table, or something around or inside it, in `aria-controls`;
//   - a tab with no panel of its own around a table: the first table after its tab list.
// And only when the press left the browser — an event or a navigation the page now waits on. A press that
// sent nothing changes no rows.

import {disown, isAwaiting, own, page, whenQuiet} from "./rask-owned.js";

const BOX = "[data-rask-skeleton]";
const WORD = "data-rask-skeleton";
const MARK = "data-skeleton";
const LOADING = "loading";
const WAITS = "data-rask-loading";

// A render that never answers does not hold the skeleton for ever.
const HARD_MS = 30000;

// How many levels above a tab list the table it switches is looked for.
const TAB_REACH = 4;

interface Held {
    readonly bodies: HTMLElement[];
    readonly styles: (string | null)[];
    readonly watch: MutationObserver | null;
    readonly hard: ReturnType<typeof setTimeout>;
}

const held = new Map<Element, Held>();

function visible(el: Element): boolean {
    return el.isConnected && el.getClientRects().length > 0;
}

function around(el: Element | null): Element | null {
    if (!el) return null;
    return el.closest(BOX) || el.querySelector(BOX);
}

// A control naming what it controls, by id.
function named(origin: Element): Element | null {
    const control = origin.closest("[aria-controls]");
    if (!control || !page) return null;
    for (const id of (control.getAttribute("aria-controls") || "").split(/\s+/)) {
        const box = id ? around(page.getElementById(id)) : null;
        if (box && visible(box)) return box;
    }
    return null;
}

// The first table after a tab list, a few levels up from it.
function switched(origin: Element): Element | null {
    const list = origin.closest("[role=tab]") ? origin.closest("[role=tablist]") : null;
    let level: Element | null = list ? list.parentElement : null;
    for (let i = 0; level && i < TAB_REACH; i++, level = level.parentElement) {
        for (const box of Array.from(level.querySelectorAll(BOX))) {
            if (list!.compareDocumentPosition(box) & Node.DOCUMENT_POSITION_FOLLOWING && visible(box)) return box;
        }
    }
    return null;
}

/** The table a press starting at `origin` asks for other rows, or null. */
export function boxFor(origin: Element | null): Element | null {
    if (!origin) return null;
    if (!origin.closest("tbody")) {
        for (let waits = origin.closest("[" + WAITS + "]"); waits; waits = waits.parentElement && waits.parentElement.closest("[" + WAITS + "]")) {
            if (waits.getAttribute(WAITS) === "off") break;
            const inside = waits.closest(BOX);
            if (inside) return inside;
            const before = waits.previousElementSibling;
            if (before && before.matches(BOX)) return before;
        }
    }
    return named(origin) || switched(origin);
}

function loading(box: Element): boolean {
    return box.getAttribute(WORD) === LOADING;
}

function letGo(box: Element): void {
    const hold = held.get(box);
    if (!hold) return;
    held.delete(box);
    clearTimeout(hold.hard);
    if (hold.watch) hold.watch.disconnect();
    hold.bodies.forEach(function (body, i) {
        const style = hold.styles[i];
        if (style === null) body.removeAttribute("style"); else body.setAttribute("style", style);
        disown(body, "style");
    });
    box.removeAttribute(MARK);
    disown(box, MARK);
}

/** Lets go of every skeleton whose page has answered and whose render says its rows are there. */
export function settle(): void {
    if (isAwaiting()) return;
    for (const box of Array.from(held.keys())) {
        if (!box.isConnected || !loading(box)) letGo(box);
    }
}

/** Shows `box`'s skeleton: its rows keep their places and draw a bar, its row groups keep their height. */
export function hold(box: Element): void {
    if (held.has(box)) return;
    const bodies = Array.from(box.querySelectorAll<HTMLElement>("tbody"));
    // Measured before anything is written, so the heights are the rows' own.
    const heights = bodies.map(function (body) { return body.getBoundingClientRect().height; });
    const styles = bodies.map(function (body) { return body.getAttribute("style"); });
    bodies.forEach(function (body, i) {
        own(body, "style");
        body.style.height = heights[i] + "px";
    });
    own(box, MARK);
    box.setAttribute(MARK, "");
    const watch = typeof MutationObserver === "function" ? new MutationObserver(settle) : null;
    if (watch) watch.observe(box, {attributes: true, attributeFilter: [WORD]});
    held.set(box, {bodies, styles, watch, hard: setTimeout(function () { letGo(box); }, HARD_MS)});
}

// After the page's own listeners: by then a press that sends has sent, and one that does not has not.
function pressed(e: Event): void {
    if (!isAwaiting()) return;
    const box = boxFor(e.target instanceof Element ? e.target : null);
    if (box) hold(box);
}

if (page && typeof window !== "undefined") {
    window.addEventListener("click", pressed);
    window.addEventListener("change", pressed);
    whenQuiet(settle);
}
