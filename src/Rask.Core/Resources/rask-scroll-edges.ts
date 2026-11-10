// A scroll area that says which of its sides hide something (data-rask-scroll-edges).
//
// A table with a sticky first or last column (Ui.TableColumn.Sticky) casts a shadow from that column once rows
// have scrolled under it, and not before — Flux UI's table, measured: its script marks the scroll area
// `data-scrolled-right` once it has moved off its left edge and `data-scrolled-left` while it has not reached its
// right one, and the sticky cells' `in-data-scrolled-*` classes draw the shadow on that mark. This is that script.
//
// The marks are PHYSICAL, as the sticky cells are (`first:left-0`, `last:right-0`): `data-scrolled-right` says
// something is hidden past the LEFT edge, `data-scrolled-left` past the RIGHT one — in a right-to-left page too,
// where the browser counts `scrollLeft` from 0 at the right edge down to minus the overflow.
//
// When: as the area scrolls (one passive listener on the document, capturing, since `scroll` does not bubble), as
// it or its table changes size (a ResizeObserver: a narrower window, rows that arrive wider), and as it arrives.
// A mark is written only when it changes, and held against the morph (rask-owned.ts).

import {own, page} from "./rask-owned.js";

const ATTR = "data-rask-scroll-edges";

/** Within this many pixels of an edge counts as AT it: a zoomed page scrolls by fractions. */
const SLACK = 1;

/**
 * Which sides of an area scrolled to `left` hide something, as [past the left edge, past the right edge], for
 * content `width` wide in a box `client` wide; `rtl` when the area runs right to left, where `left` is 0 at the
 * right edge and negative from there. Exported for the fixture.
 */
export function edges(left: number, width: number, client: number, rtl: boolean): [boolean, boolean] {
    const overflow = width - client;
    if (!(overflow > SLACK)) {
        return [false, false];
    }
    // How far the area is from its left edge, in either direction of writing.
    const fromLeft = rtl ? overflow + left : left;
    return [fromLeft > SLACK, fromLeft < overflow - SLACK];
}

function mark(el: Element, name: string, on: boolean): void {
    if (el.hasAttribute(name) !== on) {
        own(el, name);
        if (on) {
            el.setAttribute(name, "");
        } else {
            el.removeAttribute(name);
        }
    }
}

/** Marks `area` for the sides that hide something now. */
export function measure(area: HTMLElement): void {
    const [left, right] = edges(area.scrollLeft, area.scrollWidth, area.clientWidth, getComputedStyle(area).direction === "rtl");
    mark(area, "data-scrolled-right", left);
    mark(area, "data-scrolled-left", right);
}

if (page) {
    page.addEventListener("scroll", function (e) {
        const area = e.target;
        if (area instanceof HTMLElement && area.hasAttribute(ATTR)) {
            measure(area);
        }
    }, {capture: true, passive: true});

    const sized = typeof ResizeObserver === "function"
        ? new ResizeObserver(function (entries) {
            for (const entry of entries) {
                const area = entry.target.closest("[" + ATTR + "]");
                if (area instanceof HTMLElement) {
                    measure(area);
                }
            }
        })
        : null;
    const watched = new WeakSet<Element>();

    const arrive = function (area: Element): void {
        if (!(area instanceof HTMLElement) || watched.has(area)) {
            return;
        }
        watched.add(area);
        measure(area);
        if (sized) {
            sized.observe(area);
            // The area's own box does not change when its rows grow wider; its table's does.
            const inner = area.firstElementChild;
            if (inner) {
                sized.observe(inner);
            }
        }
    };
    const scan = function (node: Node): void {
        // A table the morph put in an area already watched: its width is the one that changes now.
        const host = node.parentElement && node.parentElement.closest("[" + ATTR + "]");
        if (sized && host instanceof HTMLElement && node === host.firstElementChild) {
            sized.observe(node);
            measure(host);
        }
        if (node instanceof Element) {
            if (node.hasAttribute(ATTR)) {
                arrive(node);
            }
            node.querySelectorAll("[" + ATTR + "]").forEach(arrive);
        }
    };
    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            for (const record of records) {
                if (record.type === "attributes") {
                    scan(record.target);
                } else {
                    record.addedNodes.forEach(scan);
                }
            }
        }).observe(page.documentElement, {childList: true, subtree: true, attributes: true, attributeFilter: [ATTR]});
    }
    scan(page.documentElement);
}
