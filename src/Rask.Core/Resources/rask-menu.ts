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

import {listen, named, near, page} from "./rask-owned.js";

const POINTER = "[role=menu][data-rask-menu-pointer]";
const ROW = "[role=menuitem],[role=menuitemcheckbox],[role=menuitemradio]";
const SAFE = "data-rask-safe-area";

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
        if (row && menu && menu.matches(POINTER) && row.getAttribute("aria-disabled") !== "true") {
            light(menu, row);
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
