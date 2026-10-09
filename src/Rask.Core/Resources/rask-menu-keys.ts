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

const CONTAIN = [" ", "Enter", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown"];
const ITEM = "[role=menuitem],[role=menuitemcheckbox],[role=menuitemradio]";

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
