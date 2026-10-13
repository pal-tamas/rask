// A command palette's list (data-rask-command).
//
// A palette is a search box over a list of commands: typing narrows the list, ArrowDown and ArrowUp move the
// highlight, Enter presses the highlighted command. Narrowing and the highlight only change what is DRAWN, so
// they are done here, in the key's own event, and the page is sent nothing until a command is pressed — which is
// the page's own action (Flux UI's command, measured: no request while it is opened, typed into, walked or
// closed).
//
//   <dialog data-rask-command>
//     <input role="combobox">                        the search box; focus stays in it
//     <ul role="listbox">
//       <li><button role="option" data-rask-match="New invoice">…</button></li>
//       <li role="separator"></li>
//
// * Typing hides every option whose `data-rask-match` (its text without that) does not contain what was typed,
//   whatever its case or accents, by `hidden` on the option's row — its parent, when that is not the list itself.
//   A `[role=separator]` is hidden while anything is typed: what it separated has been filtered.
// * The highlight is `aria-selected="true"` on one option and the box's `aria-activedescendant`. It starts on
//   the first option that can be picked, goes back there whenever the list is narrowed, and the two arrows move
//   it past hidden and `aria-disabled` options and around the ends.
// * Closed, the palette starts over: the box is emptied and every option is back.
//
// All of it is held against the morph (rask-owned.ts). Enter pressing the lit command and the close on a pick
// are the runtime's already (the command palette block of rask-dom.ts).

import {listen, near, own, page} from "./rask-owned.js";

const PALETTE = "[data-rask-command]";

/** `text` without case or accents: what two strings are compared by. Exported for the fixture. */
export function fold(text: string): string {
    return text.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
}

/** Whether a command called `text` stays in a list narrowed by `query`. Exported for the fixture. */
export function matches(text: string, query: string): boolean {
    return fold(text).indexOf(fold(query)) >= 0;
}

/**
 * The option an arrow lands on: the next one that way which can be picked, around the ends; `from` itself when
 * it is the only one, -1 when none can. Exported for the fixture.
 */
export function stepTo(pickable: readonly boolean[], from: number, step: 1 | -1): number {
    const count = pickable.length;
    const start = from >= 0 ? from : step > 0 ? -1 : count;
    for (let i = 1; i <= count; i++) {
        const at = (((start + step * i) % count) + count) % count;
        if (pickable[at]) {
            return at;
        }
    }
    return -1;
}

function set(el: Element, name: string, value: string | null): void {
    own(el, name);
    if (value === null) {
        el.removeAttribute(name);
    } else if (el.getAttribute(name) !== value) {
        el.setAttribute(name, value);
    }
}

function optionsOf(palette: Element): HTMLElement[] {
    return Array.prototype.slice.call(palette.querySelectorAll("[role=option]"));
}

function rowOf(option: Element): Element {
    const parent = option.parentElement;
    return parent && parent.getAttribute("role") !== "listbox" ? parent : option;
}

function pickable(option: Element): boolean {
    return !rowOf(option).hasAttribute("hidden") && option.getAttribute("aria-disabled") !== "true";
}

// Puts the highlight on `lit`, or on nothing.
function light(palette: Element, options: HTMLElement[], lit: HTMLElement | null): void {
    for (const option of options) {
        set(option, "aria-selected", option === lit ? "true" : "false");
    }
    const box = palette.querySelector("[role=combobox]");
    if (box) {
        set(box, "aria-activedescendant", lit ? lit.id : null);
    }
}

// Shows the options that match what the box says, and starts the highlight over.
function narrow(palette: Element): void {
    const box = palette.querySelector<HTMLInputElement>("input[role=combobox]");
    const query = box ? box.value : "";
    const options = optionsOf(palette);
    for (const option of options) {
        const text = option.getAttribute("data-rask-match") ?? option.textContent ?? "";
        set(rowOf(option), "hidden", matches(text, query) ? null : "");
    }
    palette.querySelectorAll("[role=separator]").forEach(function (line) {
        set(line, "hidden", query ? "" : null);
    });
    const first = stepTo(options.map(pickable), -1, 1);
    light(palette, options, first < 0 ? null : options[first]);
}

if (page) {
    listen("input", function (e) {
        const palette = near(e.target, PALETTE);
        if (palette && e.target instanceof HTMLInputElement && e.target.getAttribute("role") === "combobox") {
            narrow(palette);
        }
    });

    listen("keydown", function (e) {
        const step = e.key === "ArrowDown" ? 1 : e.key === "ArrowUp" ? -1 : 0;
        const palette = step !== 0 && !e.ctrlKey && !e.altKey && !e.metaKey ? near(e.target, PALETTE) : null;
        if (!palette) {
            return;
        }
        e.preventDefault();
        const options = optionsOf(palette);
        const at = options.findIndex(function (option) { return option.getAttribute("aria-selected") === "true"; });
        const to = stepTo(options.map(pickable), at, step === 1 ? 1 : -1);
        if (to >= 0) {
            light(palette, options, options[to]);
        }
    });

    // Opening, the first command is lit — in the task that opens it, since `toggle` comes a task later and the
    // reader's first arrow may be there before it. Closed, the palette starts over. Neither event bubbles.
    const toggled = function (e: Event): void {
        const palette = e.target instanceof Element && e.target.matches(PALETTE) ? e.target : null;
        const opening = (e as ToggleEvent).newState === "open";
        if (!palette || opening !== (e.type === "beforetoggle")) {
            return;
        }
        const box = palette.querySelector<HTMLInputElement>("input[role=combobox]");
        if (box && !opening) {
            box.value = "";
        }
        narrow(palette);
    };
    listen("beforetoggle", toggled, true);
    listen("toggle", toggled, true);
}
