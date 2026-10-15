// A time picker's list of times (data-rask-time-list): the lit row, walked by the pointer and the keys in the
// browser, as Flux UI's script walks it.
//
// Held in the component, the lit row and whether the list was open were a round trip per row the pointer
// crossed and per arrow, and a render that lit a row the pointer was on scrolled it into view, which slid
// another row under the resting pointer, which was lit in turn. Here they are attributes this module writes and
// holds against the next render (rask-owned.ts). The page renders the rows and hears a PICK: the press on a
// row, or Enter, which presses the lit one.
//
// THE MARKUP. `data-rask-time-list` on the picker's root, which holds the `[popover]` list, its rows
// (`[role=option]` with ids; a `disabled` one is passed over) and, for the button trigger, the
// `[role=combobox]` that names the lit row with `aria-activedescendant`. The row the list opens on — the chosen
// time, else the one it was told to open to, else the first — says `data-rask-time-start`.
//
// THE RULES, measured on Flux's live time picker (2026-10-14), where none of this asks its server anything:
//   * opening lights the start row and brings it into view by the least movement, the list keeping where it
//     was scrolled to; closing lights none;
//   * the pointer lights the row it is over — one the list scrolled under it as well — and never scrolls the
//     list; a row the pointer lit goes dark when the pointer leaves the list, one the keys lit stays;
//   * on the button, closed: the vertical arrows open the list where a press would; Enter does nothing. Open:
//     the vertical arrows go a row at a time over disabled ones and stop at either end — with no row lit,
//     ArrowDown lands on the first and ArrowUp on the last — and bring it into view; Enter presses the lit
//     row; Tab closes; Home, End, the page keys and letters do nothing.

import {stepFrom} from "./rask-menu-keys.js";
import {scrollBy} from "./rask-focus.js";
import {isShown, listen, near, own, page, setShown} from "./rask-owned.js";

const ROOT = "[data-rask-time-list]";
const OPTION = "[role=option]";
// What an open list keeps from the page behind it and from a form around it.
const CONTAIN = ["Enter", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown"];

interface State {
    /** The lit row's id: a render may put another element in its place. */
    id: string;
    /** The pointer lit it. */
    pointed: boolean;
}

const states = new WeakMap<Element, State>();

function stateOf(root: Element): State {
    let state = states.get(root);
    if (!state) {
        states.set(root, state = {id: "", pointed: false});
    }
    return state;
}

function listOf(root: Element): HTMLElement | null {
    return root.querySelector<HTMLElement>("[popover]");
}

function rowsOf(root: Element): HTMLElement[] {
    const list = listOf(root);
    return list ? Array.prototype.slice.call(list.querySelectorAll(OPTION)) : [];
}

function isOff(row: Element): boolean {
    return row.hasAttribute("disabled") || row.getAttribute("aria-disabled") === "true";
}

function litOf(root: Element): HTMLElement | null {
    const id = stateOf(root).id;
    const row = id && page ? page.getElementById(id) : null;
    return row && root.contains(row) ? row : null;
}

// An attribute this module writes on a rendered element and holds against the next render.
function mark(el: Element, name: string, value: string | null): void {
    own(el, name);
    if (value === null) {
        if (el.hasAttribute(name)) {
            el.removeAttribute(name);
        }
    } else if (el.getAttribute(name) !== value) {
        el.setAttribute(name, value);
    }
}

// Lights `row` and no other, and names it on the combobox.
function light(root: Element, row: HTMLElement | null, pointed: boolean = false): void {
    const state = stateOf(root);
    for (const each of rowsOf(root)) {
        mark(each, "data-active", each === row ? "" : null);
    }
    state.id = row ? row.id : "";
    state.pointed = !!row && pointed;
    const combobox = root.querySelector("[role=combobox]");
    if (combobox) {
        mark(combobox, "aria-activedescendant", row && row.id ? row.id : null);
    }
}

// The lit row brought into view inside the list by the least movement, its scroll padding counted, as a
// browser brings an element into view — and nothing else scrolls.
function reveal(root: Element, row: HTMLElement | null): void {
    const list = listOf(root);
    if (!row || !list || !isShown(list)) {
        return;
    }
    const style = getComputedStyle(list);
    const box = row.getBoundingClientRect();
    const top = list.getBoundingClientRect().top + list.clientTop;
    const by = scrollBy(
        box.top - (parseFloat(style.scrollPaddingTop) || 0), box.bottom + (parseFloat(style.scrollPaddingBottom) || 0),
        top, top + list.clientHeight);
    if (by) {
        list.scrollTop += by;
    }
}

function start(root: Element): HTMLElement | null {
    const rows = rowsOf(root);
    for (const row of rows) {
        if (row.hasAttribute("data-rask-time-start") && !isOff(row)) {
            return row;
        }
    }
    return rows[stepFrom(rows.map(isOff), -1, 1)] || null;
}

function step(root: Element, dir: 1 | -1): void {
    const rows = rowsOf(root);
    const at = litOf(root);
    const index = at ? rows.indexOf(at) : -1;
    const from = index >= 0 ? index : dir === 1 ? -1 : rows.length;
    const to = rows[stepFrom(rows.map(isOff), from, dir)];
    if (to) {
        light(root, to);
        reveal(root, to);
    }
}

function rootOfList(target: EventTarget | null): Element | null {
    const root = target instanceof Element ? target.closest(ROOT) : null;
    return root && listOf(root) === target ? root : null;
}

if (page) {
    // `beforetoggle` and `toggle` do not bubble; they are caught on the way down. Lit in the task the list
    // opens, so the first frame it is drawn in already shows the row.
    listen("beforetoggle", function (e) {
        const root = rootOfList(e.target);
        if (root) {
            light(root, (e as ToggleEvent).newState === "open" ? start(root) : null);
        }
    }, true);

    // Once it shows, and has a size: the lit row is brought into view.
    listen("toggle", function (e) {
        const root = rootOfList(e.target);
        if (root && (e as ToggleEvent).newState === "open") {
            reveal(root, litOf(root));
        }
    }, true);

    // The mouse's events: a row the list scrolled under a pointer at rest is told as a mouseover too.
    listen("mouseover", function (e) {
        const row = near(e.target, OPTION);
        const root = row ? row.closest(ROOT) : null;
        const list = root ? listOf(root) : null;
        if (!row || !root || !list || !isShown(list) || isOff(row) || litOf(root) === row) {
            return;
        }
        light(root, row, true);
    }, {capture: true, passive: true});

    listen("mouseout", function (e) {
        const list = near(e.target, "[popover]");
        const root = list ? list.closest(ROOT) : null;
        const to = e.relatedTarget;
        if (!list || !root || listOf(root) !== list || (to instanceof Node && list.contains(to))) {
            return;
        }
        if (stateOf(root).pointed) {
            light(root, null);
        }
    }, {capture: true, passive: true});

    listen("keydown", function (e) {
        const button = e.target;
        if (e.ctrlKey || e.altKey || e.metaKey || !(button instanceof HTMLElement) || button.getAttribute("role") !== "combobox") {
            return;
        }
        const root = button.closest(ROOT);
        const list = root ? listOf(root) : null;
        if (!root || !list || (button as HTMLButtonElement).disabled) {
            return;
        }
        const key = e.key;
        const vertical = key === "ArrowDown" || key === "ArrowUp";
        if (!isShown(list)) {
            if (vertical) {
                e.preventDefault();
                setShown(list, true);
            }
            return;
        }
        if (vertical) {
            step(root, key === "ArrowDown" ? 1 : -1);
        } else if (key === "Enter") {
            const lit = litOf(root);
            if (lit && !isOff(lit)) {
                lit.click();
            }
        } else if (key === "Tab") {
            setShown(list, false);
        }
        if (CONTAIN.indexOf(key) >= 0) {
            e.preventDefault();
        }
    }, true);
}

