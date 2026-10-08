// Keys a widget keeps for itself (data-rask-contain-keys), and a group one arrow walks (data-rask-roving).
//
// CONTAINMENT. A widget that handles a key in the page's own handler still has the browser's default to deal
// with: an arrow scrolls the page behind a calendar, Enter presses the button a listbox is opened from. The
// page's handler runs a round trip later and cannot cancel it. So the widget lists the keys it handles:
//
//   data-rask-contain-keys="Arrows Home End PageUp PageDown"
//
// and the default of a keydown inside it is cancelled — nothing else: the event still reaches the page.
// Names are KeyboardEvent.key's, with `Space` for the space bar and `Arrows` for the four of them. A key
// pressed with Ctrl, Alt or Meta is left alone, and so is one typed into a text field inside the widget
// (unless the field itself carries the attribute).
//
// WHY A LIST, and not a rule per role: measured on Flux UI, no two widgets keep the same keys. Its calendar
// grid keeps the arrows, Home, End and the paging keys but not Space; its colour area the same; a swatch
// listbox adds Space and Enter; a pillbox trigger keeps Enter, Space and the vertical arrows, the same
// trigger as role="button" keeps Space but not Enter, and a select's button keeps Enter but not Space. A
// role cannot say that — and a rule on `[role=grid]` would take the arrows from an app's own editable grid.
// The trees and expanded comboboxes of rask-dom.ts keep their role rules; this is for everything since.
//
// `data-rask-listbox-button` (round one) is this with "Enter ArrowUp ArrowDown", on the button itself and only
// while its `aria-expanded` is not "true". A widget whose keys change when it opens renders a different list.
//
// ROVING. `data-rask-roving` on a `[role=radiogroup]` of `[role=radio]` elements that are not native radios:
// ArrowDown / ArrowRight focus the next one and ArrowUp / ArrowLeft the one before, wrapping at both ends,
// and the one focused is PRESSED — a radio group's selection follows its focus — so the page's own click
// handler checks it. The tab stop (`tabindex="0"`) moves with the focus; Space presses the focused one.

import {listen, near, page} from "./rask-owned.js";

const CONTAIN = "data-rask-contain-keys";
const ARROWS = ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"];

/** Whether the list a widget wrote (`Arrows Home Space …`) names `key`, a KeyboardEvent.key. */
export function listsKey(list: string, key: string): boolean {
    const names = list.split(" ");
    return names.indexOf(key === " " ? "Space" : key) >= 0 || (ARROWS.indexOf(key) >= 0 && names.indexOf("Arrows") >= 0);
}

/** The radio `by` places from `index` among `count`, wrapping at both ends. */
export function rove(index: number, by: number, count: number): number {
    return count > 0 ? ((index + by) % count + count) % count : -1;
}

function typesText(el: Element): boolean {
    if (el instanceof HTMLTextAreaElement || el instanceof HTMLSelectElement) {
        return true;
    }
    if (el instanceof HTMLInputElement) {
        return ["button", "checkbox", "radio", "range", "submit", "reset", "file", "color", "image"].indexOf(el.type) < 0;
    }
    return el instanceof HTMLElement && el.isContentEditable;
}

if (page) {
    listen("keydown", function (e) {
        const t = e.target;
        if (!(t instanceof Element) || e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }
        const widget = t.closest("[" + CONTAIN + "]");
        if (widget && (widget === t || !typesText(t)) && listsKey(widget.getAttribute(CONTAIN) || "", e.key)) {
            e.preventDefault();
        } else if (t instanceof HTMLButtonElement && t.hasAttribute("data-rask-listbox-button")
            && t.getAttribute("aria-expanded") !== "true" && listsKey("Enter ArrowUp ArrowDown", e.key)) {
            e.preventDefault();
        }

        const group = near(t, "[data-rask-roving]");
        const by = e.key === "ArrowDown" || e.key === "ArrowRight" ? 1 : e.key === "ArrowUp" || e.key === "ArrowLeft" ? -1 : 0;
        if (!group || t.getAttribute("role") !== "radio" || (!by && e.key !== " ")) {
            return;
        }
        if (!by) {
            // A <button> presses itself on Space; anything else has to be pressed.
            if (!(t instanceof HTMLButtonElement)) {
                e.preventDefault();
                (t as HTMLElement).click();
            }
            return;
        }
        e.preventDefault();
        const radios = group.querySelectorAll<HTMLElement>("[role=radio]:not([disabled]):not([aria-disabled=true])");
        const next = radios[rove(Array.prototype.indexOf.call(radios, t), by, radios.length)];
        if (!next) {
            return;
        }
        for (let i = 0; i < radios.length; i++) {
            radios[i].setAttribute("tabindex", radios[i] === next ? "0" : "-1");
        }
        next.focus();
        next.click();
    }, true);
}
