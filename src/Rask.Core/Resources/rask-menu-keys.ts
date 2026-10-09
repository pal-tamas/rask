// Menus (role="menu"): the keyboard, the pick and the focus.
//
// A menu's keyboard cursor is C#'s: its key handler decides which row the cursor is on and a render writes
// `data-active` on that row. What C# cannot do is move focus, press a row or close a popover, so the rest lives
// here:
//   * FOCUS FOLLOWS THE CURSOR — the row that gains `data-active`, or the roving tab stop (tabindex="0"), is
//     focused, so a screen reader follows the arrow keys row by row (roving focus, as Flux UI's menus have it);
//     a lit row that says tabindex="-1" is the pointer's, and takes none;
//   * the navigation keys move the cursor rather than scrolling the page behind it: the C# handler still receives
//     every one — this only prevents the default;
//   * Enter / Space press the row that has focus, so its own click handler, its link, or its checkbox runs
//     exactly as a pointer would run it — except on a submenu's row, where each key means something different
//     and C# answers them;
//   * ArrowDown on a closed menu button opens it with the cursor on the first row;
//   * a pick closes the popover the menu sits in — unless the row, or something around it, says
//     data-rask-keep-open, or the row opens a submenu;
//   * Tab out of an open menu closes it, the way a menu is left;
//   * a click outside closes it (the browser's) and focus goes back to its trigger (ours).
// Escape is not touched: closing the popover on Escape is the browser's, and it hands focus back to the trigger.
// A menu that names its row with aria-activedescendant instead of focusing it is pressed the same way.
//
// The role is the whole request, as a tablist's is: a page with a `[role=menu]` loads the hooks
// (rask-hook-loader.ts), and a page with no menu does not download this.

import {isShown, listen, near, page, setShown} from "./rask-owned.js";
import {cursorOf, flyoutOf, isOff, isOpen, kept, land, ROW as ITEM, rowOf, rowsOf, setOpen} from "./rask-menu.js";

const CONTAIN = [" ", "Enter", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown"];

// ----- The cursor of a menu the runtime keeps (data-rask-menu-cursor) ------------------------------------
// Flux UI's menu, key for key (measured on its live dropdown): with focus still on the menu either arrow lands
// on the row the pointer lit, or on the first; from a row the arrows count from the LIT row — which the pointer
// may have moved — a row at a time, stopping at the ends and stepping over what is disabled; Home, End and the
// page keys are not menu keys; ArrowRight or Enter on a submenu's row opens it onto its first row, Space opens
// it and stays, ArrowLeft closes the flyout the cursor is in; a letter jumps to the row that starts with it.
// One thing is not Flux's: after the pointer has lit a row and left, Flux's keys do nothing; here they go on
// from the row that has focus.

const TYPE_AHEAD_MS = 500;
let typed = "";
let typedAt = 0;

/** The row `dir` (1 or -1) from `from` that is not off; `from` itself when there is none that way. */
export function stepFrom(off: readonly boolean[], from: number, dir: 1 | -1): number {
    for (let i = from + dir; i >= 0 && i < off.length; i += dir) {
        if (!off[i]) {
            return i;
        }
    }
    return from;
}

/**
 * Type-ahead: the index the letters typed so far land on, or -1. `texts` holds null for a row that is off.
 * One letter looks from the row after the cursor, and the same letter again goes on to the next that starts
 * with it; several different letters are a prefix, looked for from the cursor itself.
 */
export function typeAhead(buffer: string, key: string, cursor: number, texts: readonly (string | null)[]): number {
    let repeated = true;
    for (let i = 1; i < buffer.length; i++) {
        repeated = repeated && buffer[i] === buffer[0];
    }
    const needle = (repeated ? key : buffer).toLocaleLowerCase();
    const start = needle.length === 1 ? cursor + 1 : cursor;
    for (let i = 0; i < texts.length; i++) {
        const at = (((start + i) % texts.length) + texts.length) % texts.length;
        const text = texts[at];
        if (text !== null && text.toLocaleLowerCase().indexOf(needle) === 0) {
            return at;
        }
    }
    return -1;
}

// Answers a key for a kept menu. True when the key was the cursor's.
function cursorKey(root: HTMLElement, target: HTMLElement, key: string): boolean {
    const onRow = target.matches(ITEM);
    const at = cursorOf(root) || (onRow ? target : null);
    const level = at ? at.closest("[role=menu]") || root : root;
    const rows = rowsOf(level);
    const off = rows.map(isOff);
    const index = at ? rows.indexOf(at) : -1;
    const flyout = at && !isOff(at) ? flyoutOf(at) : null;

    if (key === "ArrowDown" || key === "ArrowUp") {
        const to = !onRow ? (at || rows[stepFrom(off, -1, 1)]) : rows[stepFrom(off, index, key === "ArrowDown" ? 1 : -1)];
        if (to) {
            land(root, to);
        }
        return true;
    }
    if (flyout && at && (key === "ArrowRight" || key === "Enter" || key === " ")) {
        const inside = rowsOf(flyout);
        const first = key === " " ? undefined : inside[stepFrom(inside.map(isOff), -1, 1)];
        // Shown before focus goes into it; and landing on the row itself shuts its flyout, so it opens after.
        setOpen(at, true);
        land(root, first || at);
        setOpen(at, true);
        return true;
    }
    if (key === "ArrowLeft" && at && level !== root) {
        // Landing on the row that opened it closes the flyout: the row is not inside it.
        const opener = rowOf(level);
        if (opener) {
            land(root, opener);
        }
        return true;
    }
    if (key.length === 1 && key !== " ") {
        const now = Date.now();
        typed = now - typedAt > TYPE_AHEAD_MS ? key : typed + key;
        typedAt = now;
        const hit = typeAhead(typed, key, index, rows.map(function (row, i) { return off[i] ? null : (row.textContent || "").trim(); }));
        if (hit >= 0) {
            land(root, rows[hit]);
        }
        return true;
    }
    return false;
}

// A row that opens a submenu: it says so (aria-haspopup="menu"), or — as Flux UI's rows do, which carry no
// ARIA of their own — the menu it opens is the element right after it.
function opensSubmenu(row: Element): boolean {
    const next = row.nextElementSibling;
    return row.getAttribute("aria-haspopup") === "menu" || (!!next && next.getAttribute("role") === "menu");
}

// The menu a key belongs to: the element itself, or the menu around the row that has focus.
function menuOf(t: HTMLElement): Element | null {
    if (t.getAttribute("role") === "menu") {
        return t;
    }
    return t.matches(ITEM) ? t.closest("[role=menu]") : null;
}

if (page) {
    const doc = page;

    listen("keydown", function (e) {
        if (e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }
        const t = e.target;
        if (!(t instanceof HTMLElement)) {
            return;
        }

        const menu = menuOf(t);
        const root = kept(menu);
        if (root && cursorKey(root, t, e.key)) {
            if (CONTAIN.indexOf(e.key) >= 0) {
                e.preventDefault();
            }
            return;
        }
        if (menu && CONTAIN.indexOf(e.key) >= 0) {
            e.preventDefault();
            if (e.key === "Enter" || e.key === " ") {
                const id = menu.getAttribute("aria-activedescendant");
                const row = t !== menu ? t : (id ? doc.getElementById(id) : null);
                if (row && menu.contains(row) && row.getAttribute("aria-disabled") !== "true"
                    && !opensSubmenu(row)) {
                    row.click();
                }
            }
            return;
        }

        // A menu button is the invoker of a popover that is, or holds, a menu. Flux UI says aria-haspopup="true"
        // on it whatever it opens, so the attribute's value does not tell; the panel does.
        if (e.key === "ArrowDown"
            && t.hasAttribute("aria-haspopup") && t.hasAttribute("popovertarget")
            && t.getAttribute("aria-expanded") !== "true") {
            const panel = doc.getElementById(t.getAttribute("popovertarget") || "");
            const opened = panel && (panel.getAttribute("role") === "menu" ? panel : panel.querySelector("[role=menu]"));
            if (!opened) {
                return;
            }
            e.preventDefault();
            t.click();
            // The same key, handed to the menu it just opened: its handler puts the cursor on the first row.
            if (typeof KeyboardEvent === "function") {
                opened.dispatchEvent(new KeyboardEvent("keydown", {key: "ArrowDown", bubbles: true}));
            }
        }
    }, true);

    listen("click", function (e) {
        const row = near(e.target, ITEM);
        const root = kept(row);
        if (row && root && !isOff(row)) {
            // A press puts the cursor on its row. On a submenu's row a tap, which has no hover, opens the flyout
            // and the next one closes it; a press from a pointer that was resting there leaves it open.
            const toggles = (e as PointerEvent).pointerType === "touch" && isOpen(row);
            land(root, row);
            setOpen(row, !toggles);
        }
        if (!row || opensSubmenu(row) || row.getAttribute("aria-disabled") === "true"
            || row.closest("[data-rask-keep-open]")) {
            return;
        }
        const panel = row.closest("[popover]");
        if (!panel || !row.closest("[role=menu]")) {
            return;
        }
        // After this click has been dispatched to its own handler, not before: hiding first would move focus
        // back to the trigger ahead of the handler that needs the row.
        setTimeout(function () { setShown(panel, false); }, 0);
    });

    listen("focusout", function (e) {
        const from = e.target instanceof HTMLElement ? menuOf(e.target) : null;
        const next = e.relatedTarget;
        if (!from || !(next instanceof Element)) {
            return;
        }
        const panel = from.closest("[popover]");
        // Focus went somewhere a Tab took it: leaving the menu closes it. The trigger itself is left alone — the
        // click on it is already toggling the popover, and Shift+Tab back to it leaves the menu open, as Flux does.
        if (panel && !panel.contains(next) && next.getAttribute("popovertarget") !== panel.id) {
            setShown(panel, false);
        }
    });

    // The browser hands focus back to the trigger when Escape closes a popover, and not when a click outside
    // does: that leaves it on the page, and a keyboard user back at the top. So a popover that closes with focus
    // nowhere hands it to the button that opens it. `toggle` does not bubble; it is caught on the way down.
    listen("toggle", function (e) {
        const panel = e.target;
        if (!(panel instanceof HTMLElement) || !panel.id || (e as ToggleEvent).newState !== "closed"
            || (doc.activeElement && doc.activeElement !== doc.body)) {
            return;
        }
        const invokers = doc.querySelectorAll<HTMLElement>("[popovertarget][aria-haspopup]");
        for (let i = 0; i < invokers.length; i++) {
            if (invokers[i].getAttribute("popovertarget") === panel.id) {
                invokers[i].focus();
                return;
            }
        }
    }, true);

    if (typeof MutationObserver === "function") {
        // Focus follows the cursor. Only inside an open menu, so a render of a closed one cannot steal focus —
        // and not onto a row that says tabindex="-1" while it is lit: that one the POINTER lit, and on Flux UI
        // the pointer lights a row without taking focus from where the keyboard left it. The row a render gives
        // the tab stop (tabindex="0") is the keyboard's, lit or not.
        new MutationObserver(function (records) {
            for (const record of records) {
                const row = record.target;
                if (!(row instanceof HTMLElement) || !row.matches(ITEM) || doc.activeElement === row) {
                    continue;
                }
                const stop = row.getAttribute("tabindex");
                if ((record.attributeName === "tabindex" ? stop === "0" : row.hasAttribute("data-active") && stop !== "-1")
                    && isShown(row.closest("[popover]"))) {
                    row.focus();
                }
            }
        }).observe(doc.documentElement, {subtree: true, attributes: true, attributeFilter: ["data-active", "tabindex"]});
    }
}
