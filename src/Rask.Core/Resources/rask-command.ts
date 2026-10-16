// A command palette (data-rask-command): Flux UI's flux:command, kept in the browser.
//
// A palette is a search box over a list of commands: typing narrows the list, the arrows and the pointer move the
// lit command, Enter presses it. All of that only changes what is DRAWN, so it is done here, in the event's own
// task, and the page is sent nothing until a command is pressed — which is the page's own action (Flux's command,
// measured: no request while it is typed into, walked or left).
//
//   <div data-rask-command>
//     <input>                                         the search box; focus stays in it
//     <div role="listbox">
//       <div role="option" data-rask-match="Assign to…">…</div>
//       <div data-rask-command-empty data-hidden>…</div>
//
// What Flux does, measured on its page, and so what this does:
// * Focus in the box selects what it holds and lights the first command shown; focus leaving it lights none.
// * Typing hides, by `data-hidden`, every command whose `data-rask-match` does not contain what was typed — whatever
//   its case or accents — shows the empty row while none is left, and lights the first one left.
// * ArrowDown and ArrowUp light the next command shown that way and stop at the ends. Home and End are the box's.
// * The pointer over a command lights it; leaving the commands lights none, unless an arrow has lit one since.
// * A press on a command leaves focus on the list, which takes the arrows and does nothing with them.
// * Enter presses the lit command. A command pressed — by Enter or a click — empties the box and lights the first
//   command again, and nothing else: the palette stays open, and Escape is the modal's.
// The lit command is `data-active` on it and `aria-activedescendant` on the box. All of it is held against the morph
// (rask-owned.ts).

import {listen, near, own, page} from "./rask-owned.js";

const PALETTE = "[data-rask-command]";
const OPTION = "[role=option]";

/** `text` without case or accents: what two strings are compared by. Exported for the fixture. */
export function fold(text: string): string {
    return text.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
}

/** Whether a command called `text` stays in a list narrowed by `query`. Exported for the fixture. */
export function matches(text: string, query: string): boolean {
    return fold(text).indexOf(fold(query)) >= 0;
}

/**
 * The command an arrow lights: the next one shown that way, stopping at the ends — `from` itself when there is
 * none further, the first (or, going up, the last) shown when nothing is lit, -1 when none is shown. Exported for
 * the fixture.
 */
export function stepTo(shown: readonly boolean[], from: number, step: 1 | -1): number {
    if (from < 0) {
        const order = shown.map(function (_, i) { return i; });
        const found = (step > 0 ? order : order.reverse()).filter(function (i) { return shown[i]; });
        return found.length ? found[0] : -1;
    }
    for (let at = from + step; at >= 0 && at < shown.length; at += step) {
        if (shown[at]) {
            return at;
        }
    }
    return from;
}

function set(el: Element, name: string, value: string | null): void {
    own(el, name);
    if (value === null) {
        el.removeAttribute(name);
    } else if (el.getAttribute(name) !== value) {
        el.setAttribute(name, value);
    }
}

function boxOf(palette: Element): HTMLInputElement | null {
    return palette.querySelector<HTMLInputElement>("input");
}

function optionsOf(palette: Element): HTMLElement[] {
    return Array.prototype.slice.call(palette.querySelectorAll(OPTION));
}

function shown(option: Element): boolean {
    return !option.hasAttribute("data-hidden");
}

// The commands the pointer lit, rather than the keys or a search: only those go out with it.
const pointed = new WeakSet<Element>();

// Lights `lit`, or nothing.
function light(palette: Element, lit: HTMLElement | null): void {
    for (const option of optionsOf(palette)) {
        set(option, "data-active", option === lit ? "" : null);
        pointed.delete(option);
    }
    const box = boxOf(palette);
    if (box) {
        set(box, "aria-activedescendant", lit && lit.id ? lit.id : null);
    }
}

function first(palette: Element): HTMLElement | null {
    return optionsOf(palette).filter(shown)[0] || null;
}

// Shows the commands that match what the box says, and lights the first of them.
function narrow(palette: Element): void {
    const box = boxOf(palette);
    const query = box ? box.value : "";
    let left = 0;
    for (const option of optionsOf(palette)) {
        const keep = matches(option.getAttribute("data-rask-match") ?? option.textContent ?? "", query);
        set(option, "data-hidden", keep ? null : "");
        left += keep ? 1 : 0;
    }
    const empty = palette.querySelector("[data-rask-command-empty]");
    if (empty) {
        set(empty, "data-hidden", left ? "" : null);
    }
    light(palette, first(palette));
}

function inBox(target: EventTarget | null): HTMLElement | null {
    const palette = near(target, PALETTE);
    return palette && target instanceof HTMLInputElement && boxOf(palette) === target ? palette : null;
}

if (page) {
    listen("input", function (e) {
        const palette = inBox(e.target);
        if (palette) {
            narrow(palette);
        }
    });

    listen("focusin", function (e) {
        const palette = inBox(e.target);
        if (palette) {
            (e.target as HTMLInputElement).select();
            light(palette, first(palette));
        }
    });

    listen("focusout", function (e) {
        const palette = inBox(e.target);
        if (palette) {
            light(palette, null);
        }
    });

    listen("keydown", function (e) {
        // The list itself, focused by a press on a command, takes the arrows and does nothing with them, as Flux's.
        const list = e.key === "ArrowDown" || e.key === "ArrowUp" ? near(e.target, PALETTE + " [role=listbox]") : null;
        if (list && e.target === list) {
            e.preventDefault();
            return;
        }
        const palette = !e.ctrlKey && !e.altKey && !e.metaKey && !e.isComposing ? inBox(e.target) : null;
        if (!palette) {
            return;
        }
        const options = optionsOf(palette);
        const at = options.findIndex(function (option) { return option.hasAttribute("data-active"); });
        if (e.key === "Enter") {
            // A palette is no form field: Enter presses a command, and never submits a form around it.
            e.preventDefault();
            if (at >= 0) {
                options[at].click();
            }
            return;
        }
        const step = e.key === "ArrowDown" ? 1 : e.key === "ArrowUp" ? -1 : 0;
        if (step === 0) {
            return;
        }
        e.preventDefault();
        const to = stepTo(options.map(shown), at, step);
        if (to >= 0) {
            light(palette, options[to]);
        }
    });

    listen("mouseover", function (e) {
        const option = near(e.target, OPTION);
        const palette = option ? near(option, PALETTE) : null;
        if (palette && option && !option.hasAttribute("data-active")) {
            light(palette, option);
            pointed.add(option);
        }
    });

    // Only the command the POINTER lit goes out with it: one an arrow has lit since stays.
    listen("mouseout", function (e) {
        const option = near(e.target, OPTION);
        const palette = option ? near(option, PALETTE) : null;
        if (palette && option && pointed.has(option) && option.hasAttribute("data-active") && near(e.relatedTarget, OPTION) === null) {
            light(palette, null);
        }
    });

    // A command pressed empties the box (its own handler hears the click as it is): the list starts over.
    listen("click", function (e) {
        const option = near(e.target, OPTION);
        const palette = option ? near(option, PALETTE) : null;
        const box = palette ? boxOf(palette) : null;
        if (!palette || !box || box.value === "") {
            return;
        }
        box.value = "";
        narrow(palette);
    });
}
