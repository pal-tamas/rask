// A menu's pointer, made one thing with its keyboard cursor.
//
// The cursor is the page's: its key handler decides which row it is on and a render writes data-active there.
// Left at that, a menu has two highlights — the row the arrows reached and the row under the pointer — and
// over a socket the second one trails the pointer by a round trip. Two attributes close the gap:
//
//   * data-rask-menu-pointer on the [role=menu]: the row the pointer is over gets data-active at once and
//     every other row of that menu loses it; when the pointer leaves the menu, none has it — but the row
//     that has FOCUS, which is the keyboard's. The page moves its own cursor from the row's pointerenter
//     handler, as it would anyway, and its render then agrees with what is already on screen. One row is
//     ever lit, and the arrows continue from it. Focus is not moved: on Flux UI the pointer lights a row and
//     document.activeElement stays where it was.
//   * data-rask-safe-area on a row that opens a submenu — ="<flyout id>", or with no value when the flyout
//     is the element right after the row: while that flyout is open, the
//     triangle between the pointer and the flyout's near edge belongs to the row. Without it the diagonal
//     towards the flyout crosses the rows below, each one closes the submenu, and the flyout is gone
//     before the pointer arrives. The corner follows the pointer along the row; past the row it stays
//     where it was, so a move that is not towards the flyout leaves the triangle and reaches the next row.
//     (Measured on Flux UI's dropdown: no timer — the area holds for as long as the pointer is inside it.)
//
// The triangle is a real element — a child of the row, so being over it IS being over the row, to :hover and
// to every pointer handler alike — clipped to shape and marked data-rask-managed so a render leaves it be.
//
// A MENU WHOSE CURSOR THE RUNTIME KEEPS (data-rask-menu-cursor on the outermost [role=menu]). Then nothing
// about where the reader is in the menu is the page's at all: which row is lit, which has focus and the tab
// stop, and which flyouts are open are attributes this module and rask-menu-keys.ts write and hold against the
// morph (rask-owned.ts), from the pointer and the keys, in the browser. A pointer gliding down the menu and an
// arrow key held down send nothing to the page — which hears a row when it is PRESSED, and the popover's toggle.
// That is how Flux UI's menu works, and the rules are the ones measured there:
//   * the pointer lights the row it enters and does not move focus; it opens the flyout of a submenu's row and
//     closes every flyout the row is not inside; a flyout stays open after the pointer has left the menu;
//   * the keyboard's row is lit and focused; while it is inside a flyout the submenu's row stays lit too;
//   * an open flyout is `data-open` on the element that holds a submenu's row and its flyout.
// Everything is forgotten when the popover the menu sits in closes.

import {disown, listen, named, near, own, page} from "./rask-owned.js";

const POINTER = "[role=menu][data-rask-menu-pointer]";
/** A menu row of any kind. */
export const ROW = "[role=menuitem],[role=menuitemcheckbox],[role=menuitemradio]";
const SAFE = "data-rask-safe-area";
const KEPT = "[data-rask-menu-cursor]";

// The row lit last in a kept menu, by the pointer or the keys: where the arrows count from.
const cursors = new WeakMap<Element, HTMLElement>();

// An attribute the runtime writes on a rendered element and holds against the next render.
function mark(el: Element, name: string, value: string | null): void {
    if (value === null) {
        el.removeAttribute(name);
        disown(el, name);
        return;
    }
    own(el, name);
    if (el.getAttribute(name) !== value) {
        el.setAttribute(name, value);
    }
}

// The roving tab stop: 0 on the row that has focus, and back to the -1 every row is rendered with.
function stop(row: Element, on: boolean): void {
    if (on) {
        mark(row, "tabindex", "0");
    } else if (row.getAttribute("tabindex") === "0") {
        row.setAttribute("tabindex", "-1");
        disown(row, "tabindex");
    }
}

/** The outermost menu around `el` whose cursor the runtime keeps, or null. */
export function kept(el: Element | null): HTMLElement | null {
    return el ? el.closest<HTMLElement>(KEPT) : null;
}

/** A row the cursor steps over. */
export function isOff(row: Element): boolean {
    return (row as HTMLButtonElement).disabled === true || row.getAttribute("aria-disabled") === "true";
}

/** The rows of one menu, without those of the flyouts inside it. */
export function rowsOf(menu: Element): HTMLElement[] {
    const rows: HTMLElement[] = [];
    const all = menu.querySelectorAll<HTMLElement>(ROW);
    for (let i = 0; i < all.length; i++) {
        if (all[i].closest("[role=menu]") === menu) {
            rows.push(all[i]);
        }
    }
    return rows;
}

/**
 * The flyout a submenu's row opens: named by its safe area, or — as Flux UI's rows have it — the menu right
 * after the row. Null for a row that opens nothing.
 */
export function flyoutOf(row: Element): Element | null {
    const next = row.getAttribute(SAFE) ? named(row, SAFE) : row.nextElementSibling;
    return next && next.getAttribute("role") === "menu" ? next : null;
}

/** The row whose flyout `menu` is, or null for a menu that is nobody's flyout. */
export function rowOf(menu: Element): HTMLElement | null {
    const before = menu.previousElementSibling;
    if (before instanceof HTMLElement && before.matches(ROW)) {
        return before;
    }
    return menu.id && page ? page.querySelector<HTMLElement>("[" + SAFE + '="' + menu.id + '"]') : null;
}

/** Opens or closes the flyout of a submenu's row: `data-open` on the element that holds them both. */
export function setOpen(row: Element, open: boolean): void {
    const flyout = flyoutOf(row);
    if (!flyout || !row.parentElement) {
        return;
    }
    mark(row.parentElement, "data-open", open ? "" : null);
    if (!open) {
        // What was lit or focused in there is gone with it.
        const inside = flyout.querySelectorAll(ROW);
        for (let i = 0; i < inside.length; i++) {
            mark(inside[i], "data-active", null);
            stop(inside[i], false);
        }
    }
}

/** Whether the flyout of a submenu's row is open. */
export function isOpen(row: Element): boolean {
    return !!row.parentElement && row.parentElement.hasAttribute("data-open");
}

// Every flyout `row` is not inside closes.
function closeAround(root: Element, row: Element): void {
    const open = root.querySelectorAll("[data-open]");
    for (let i = 0; i < open.length; i++) {
        const opener = open[i].querySelector(ROW);
        const flyout = opener && opener.parentElement === open[i] ? flyoutOf(opener) : null;
        if (opener && flyout && !flyout.contains(row)) {
            setOpen(opener, false);
        }
    }
}

/** The row the arrows count from in a kept menu: the one lit last, while it is still lit. */
export function cursorOf(root: Element): HTMLElement | null {
    const row = cursors.get(root);
    return row && row.isConnected && row.hasAttribute("data-active") ? row : null;
}

/**
 * The keyboard, or a press, arrives on `row` of a kept menu: it is lit, focused and the tab stop, the rows of
 * the submenus around it stay lit, and every flyout it is not inside closes.
 */
export function land(root: Element, row: HTMLElement): void {
    const around: Element[] = [];
    for (let menu = row.closest("[role=menu]"); menu && menu !== root;) {
        const opener = rowOf(menu);
        if (!opener) {
            break;
        }
        around.push(opener);
        menu = opener.closest("[role=menu]");
    }
    closeAround(root, row);
    const rows = root.querySelectorAll(ROW);
    for (let i = 0; i < rows.length; i++) {
        mark(rows[i], "data-active", rows[i] === row || around.indexOf(rows[i]) >= 0 ? "" : null);
        stop(rows[i], rows[i] === row);
    }
    cursors.set(root, row);
    row.focus();
}

// The pointer enters `row` of a kept menu. The row is lit already (light, below).
function point(root: Element, row: HTMLElement): void {
    const rows = root.querySelectorAll(ROW);
    for (let i = 0; i < rows.length; i++) {
        mark(rows[i], "data-active", rows[i] === row ? "" : null);
    }
    cursors.set(root, row);
    closeAround(root, row);
    if (!isOff(row)) {
        setOpen(row, true);
    }
    // Focus that was in a flyout which has just closed goes to the row that opened it, as on Flux UI.
    let held = page!.activeElement;
    while (held instanceof HTMLElement && root.contains(held) && held.matches(ROW) && !held.getClientRects().length) {
        const menu = held.closest("[role=menu]");
        const opener: HTMLElement | null = menu ? rowOf(menu) : null;
        if (!opener) {
            break;
        }
        stop(held, false);
        stop(opener, true);
        opener.focus();
        held = opener;
    }
}

/** Forgets where the reader was in a kept menu: nothing lit, no tab stop, every flyout shut. */
export function forget(root: Element): void {
    cursors.delete(root);
    const rows = root.querySelectorAll(ROW);
    for (let i = 0; i < rows.length; i++) {
        mark(rows[i], "data-active", null);
        stop(rows[i], false);
    }
    const open = root.querySelectorAll("[data-open]");
    for (let i = 0; i < open.length; i++) {
        mark(open[i], "data-open", null);
    }
}

function light(menu: Element, row: Element | null): void {
    const lit = menu.querySelectorAll("[data-active]");
    for (let i = 0; i < lit.length; i++) {
        if (lit[i] !== row && lit[i].closest("[role=menu]") === menu) {
            lit[i].removeAttribute("data-active");
        }
    }
    if (row && !row.hasAttribute("data-active")) {
        row.setAttribute("data-active", "");
    }
}

/**
 * The triangle from the pointer to the near edge of a flyout, as the box that holds it and the polygon
 * inside that box. Exported for the fixture: the geometry is the part worth pinning without a browser.
 */
export function safeArea(
    x: number, y: number, flyout: {left: number; right: number; top: number; bottom: number},
): {left: number; top: number; width: number; height: number; clip: string} {
    // The flyout's near edge is the one facing the pointer: its left when it opened to the right.
    const edge = x <= flyout.left ? flyout.left : flyout.right;
    const left = Math.min(x, edge);
    const top = Math.min(y, flyout.top);
    const width = Math.abs(edge - x);
    const height = Math.max(y, flyout.bottom) - top;
    const apexX = x <= flyout.left ? 0 : width;
    const edgeX = width - apexX;
    return {
        left, top, width, height,
        clip: "polygon(" + apexX + "px " + (y - top) + "px, " + edgeX + "px " + (flyout.top - top) + "px, "
            + edgeX + "px " + (flyout.bottom - top) + "px)",
    };
}

let shield: HTMLElement | null = null;
// The safe-area row the pointer is on, so the pointermove listener is one null test everywhere else.
let armed: HTMLElement | null = null;

function drop(): void {
    if (shield) {
        shield.remove();
        shield = null;
    }
}

function place(row: HTMLElement, flyout: Element, x: number, y: number): void {
    if (!shield || shield.parentNode !== row) {
        drop();
        shield = page!.createElement("span");
        shield.setAttribute("data-rask-managed", "");
        shield.setAttribute("aria-hidden", "true");
        shield.style.cssText = "position:fixed;display:block;left:0;top:0;width:0;height:0;z-index:2147483647";
        row.appendChild(shield);
    }
    const area = safeArea(x, y, flyout.getBoundingClientRect());
    // `fixed` is measured from the viewport unless something above the row transforms — a menu that scales in
    // does. Where the element actually landed says which, and by how much.
    const s = shield.style;
    s.left = s.top = "0";
    s.width = s.height = "0";
    const origin = shield.getBoundingClientRect();
    s.left = (area.left - origin.left) + "px";
    s.top = (area.top - origin.top) + "px";
    s.width = area.width + "px";
    s.height = area.height + "px";
    s.clipPath = area.clip;
}

if (page) {
    listen("pointerover", function (e) {
        const row = near(e.target, ROW);
        const menu = row ? row.closest("[role=menu]") : null;
        const from = (e as PointerEvent).relatedTarget;
        // On ENTERING the row, not on moving about inside it: with the arrows gone on to another row, Flux's
        // pointer takes the light back only by leaving and coming in again.
        if (row && menu && menu.matches(POINTER) && row.getAttribute("aria-disabled") !== "true"
            && !(from instanceof Node && row.contains(from))) {
            light(menu, row);
            const root = kept(menu);
            // A touch reports one enter for the tap that follows it, which is not hovering.
            if (root && (e as PointerEvent).pointerType !== "touch") {
                point(root, row);
            }
        }
        armed = near(e.target, "[" + SAFE + "]");
    }, {capture: true, passive: true});

    listen("pointerout", function (e) {
        const menu = near(e.target, POINTER);
        const to = (e as PointerEvent).relatedTarget;
        if (menu && !(to instanceof Node && menu.contains(to))) {
            // What the pointer lit goes dark. The row the keyboard is on — the one with focus — stays lit:
            // on Flux the pointer leaving takes nothing from the arrows.
            const held = page!.activeElement;
            light(menu, held && held.hasAttribute("data-active") && held.closest("[role=menu]") === menu ? held : null);
        }
        // The pointer left the row and its triangle: for the flyout, or for somewhere that is not towards it.
        const row = near(e.target, "[" + SAFE + "]");
        if (row && shield && shield.parentNode === row && !(to instanceof Node && row.contains(to))) {
            drop();
        }
    }, {capture: true, passive: true});

    // The popover a kept menu sits in has closed: the next opening starts with no row chosen. `toggle` does not
    // bubble; it is caught on the way down.
    listen("toggle", function (e) {
        const panel = e.target;
        if (!(panel instanceof Element) || (e as ToggleEvent).newState !== "closed") {
            return;
        }
        if (panel.matches(KEPT)) {
            forget(panel);
        }
        const inside = panel.querySelectorAll(KEPT);
        for (let i = 0; i < inside.length; i++) {
            forget(inside[i]);
        }
    }, true);

    listen("pointermove", function (e) {
        const row = armed;
        if (!row) {
            return;
        }
        // Named by id, or — written with no value — the element right after the row, which is where Flux UI's
        // submenu keeps its flyout. Open is "has a box": a popover, a dialog or a plain element shown by a class
        // all answer the same.
        const flyout = row.getAttribute(SAFE) ? named(row, SAFE) : row.nextElementSibling;
        if (!flyout || !flyout.getClientRects().length) {
            drop();
            return;
        }
        // The corner follows the pointer along the row itself and nowhere else: over the triangle it stays.
        const box = row.getBoundingClientRect();
        if (e.clientX >= box.left && e.clientX <= box.right && e.clientY >= box.top && e.clientY <= box.bottom) {
            place(row, flyout, e.clientX, e.clientY);
        }
    }, {capture: true, passive: true});
}
