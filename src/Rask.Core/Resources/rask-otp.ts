// One-time-code cells (data-rask-otp).
//
// A group of one-character inputs that behaves as one field: a character moves on to the next cell, Backspace
// walks back, the arrows stop at the first empty cell, a paste fills from the first cell. Flux UI's otp input,
// key for key (measured on its live page).
//
// WHY THE CELLS ARE NOT BOUND. Driven from the page — a handler per cell, focus moved by a reply — the control
// drops characters as soon as keys arrive faster than a round trip: the second key lands in the cell the
// first one is still in, and the render that answers key one writes its idea of the cells over what keys two
// and three have typed since. So the cells are the BROWSER's: rendered with no `value` and no handler (the
// morph leaves a value it never rendered alone), moved between here, synchronously, inside the key's own
// event. The group holds ONE bound field — an <input type="hidden"> — which this keeps equal to the cells
// joined together and announces with an `input` and a `change` event, so the page binds one string the usual
// way. When the page changes that string itself (clearing a wrong code), the hidden field's value changes and
// the cells are refilled from it. The bound field and its late echo are rask-bound.ts's.
//
// data-rask-otp="numeric" (the default) | "alpha" | "alphanumeric" says what a cell accepts.

import {announce, bind} from "./rask-bound.js";
import {listen, page} from "./rask-owned.js";

const GROUP = "[data-rask-otp]";

function accepts(group: Element, c: string): boolean {
    const mode = group.getAttribute("data-rask-otp");
    return (mode === "alpha" ? /^[a-zA-Z]$/ : mode === "alphanumeric" ? /^[a-zA-Z0-9]$/ : /^[0-9]$/).test(c);
}

/** The characters of `text` a group in `mode` accepts, at most `length` of them. Exported for the fixture. */
export function otpChars(text: string, mode: string | null, length: number): string {
    const fits = mode === "alpha" ? /[a-zA-Z]/ : mode === "alphanumeric" ? /[a-zA-Z0-9]/ : /[0-9]/;
    let out = "";
    for (let i = 0; i < text.length && out.length < length; i++) {
        if (fits.test(text[i])) {
            out += text[i];
        }
    }
    return out;
}

function cellsOf(group: Element): HTMLInputElement[] {
    return Array.prototype.slice.call(group.querySelectorAll("input:not([type=hidden])"));
}

function join(cells: HTMLInputElement[]): string {
    let value = "";
    for (const cell of cells) {
        value += cell.value;
    }
    return value;
}

// Writes `value` into the cells from the first one: a code has no gaps, so deleting closes up to the left.
function fill(cells: HTMLInputElement[], value: string): void {
    for (let i = 0; i < cells.length; i++) {
        const c = value[i] || "";
        if (cells[i].value !== c) {
            cells[i].value = c;
        }
    }
}

function land(cells: HTMLInputElement[], index: number): void {
    const filled = join(cells).length;
    const cell = cells[Math.max(0, Math.min(index, filled, cells.length - 1))];
    if (!cell) {
        return;
    }
    if (page!.activeElement !== cell) {
        cell.focus();
    }
    cell.select();
}

// Rewrites the whole code and puts the caret on cell `index`.
function commit(group: Element, cells: HTMLInputElement[], value: string, index: number): void {
    fill(cells, value);
    land(cells, index);
    announce(group, join(cells));
}

// The page changed the code (the bound field's value moved, or the group just arrived): the cells follow it.
bind(GROUP, function (group) {
    return join(cellsOf(group));
}, function (group, rendered) {
    const cells = cellsOf(group);
    fill(cells, otpChars(rendered, group.getAttribute("data-rask-otp"), cells.length));
});

if (page) {
    const doc = page;

    listen("beforeinput", function (e) {
        const cell = e.target;
        const group = cell instanceof HTMLInputElement ? cell.closest(GROUP) : null;
        const data = (e as InputEvent).data;
        // One character that does not fit is refused before it shows. Longer text (a paste, an autofilled
        // code) is sorted out once it has arrived.
        if (group && data && data.length === 1 && !accepts(group, data)) {
            e.preventDefault();
        }
    }, true);

    listen("input", function (e) {
        const cell = e.target;
        const group = cell instanceof HTMLInputElement && cell.type !== "hidden" ? cell.closest(GROUP) : null;
        if (!group || (e as InputEvent).isComposing) {
            return;
        }
        const cells = cellsOf(group);
        const index = cells.indexOf(cell as HTMLInputElement);
        const typed = otpChars((cell as HTMLInputElement).value, group.getAttribute("data-rask-otp"), cells.length);
        if (typed.length > 1 && (e as InputEvent).inputType !== "insertText") {
            // A whole code in one cell: the browser's one-time-code autofill, or a paste that reached here.
            commit(group, cells, typed, typed.length);
            return;
        }
        // The last character typed is the cell's: typing over a filled cell replaces it.
        let value = join(cells.slice(0, index));
        value += typed.slice(-1) + join(cells.slice(index + 1));
        commit(group, cells, value, typed ? index + 1 : index);
    }, true);

    listen("keydown", function (e) {
        const cell = e.target;
        const group = cell instanceof HTMLInputElement && cell.type !== "hidden" ? cell.closest(GROUP) : null;
        if (!group || e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }
        const cells = cellsOf(group);
        const index = cells.indexOf(cell as HTMLInputElement);
        const value = join(cells);
        if (e.key === "Backspace" || e.key === "Delete") {
            e.preventDefault();
            // Backspace steps back as it deletes; in an empty cell it only steps. Delete stays where it is.
            const next = index < value.length ? value.slice(0, index) + value.slice(index + 1) : value;
            commit(group, cells, next, e.key === "Backspace" ? index - 1 : index);
        } else if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
            e.preventDefault();
            land(cells, index + (e.key === "ArrowLeft" ? -1 : 1));
        } else if (e.key === "ArrowUp" || e.key === "ArrowDown" || e.key === "Home" || e.key === "End") {
            e.preventDefault(); // nothing, as there: the caret has nowhere to go inside one character
        }
    }, true);

    listen("paste", function (e) {
        const group = e.target instanceof Element ? e.target.closest(GROUP) : null;
        const data = (e as ClipboardEvent).clipboardData;
        if (!group || !data) {
            return;
        }
        e.preventDefault();
        const cells = cellsOf(group);
        // From the first cell, whichever one has focus, replacing what was there.
        const code = otpChars(data.getData("text"), group.getAttribute("data-rask-otp"), cells.length);
        if (code) {
            commit(group, cells, code, code.length);
        }
    }, true);

    // A press past the first empty cell lands on it, and whatever cell takes focus has its character selected,
    // so the next key replaces it.
    listen("focusin", function (e) {
        const cell = e.target;
        const group = cell instanceof HTMLInputElement && cell.type !== "hidden" ? cell.closest(GROUP) : null;
        if (group) {
            const cells = cellsOf(group);
            land(cells, cells.indexOf(cell as HTMLInputElement));
        }
    }, true);
    // The press that follows the focus would put a caret where it landed and drop the selection.
    listen("pointerup", function (e) {
        const cell = e.target;
        if (cell instanceof HTMLInputElement && cell === doc.activeElement && cell.closest(GROUP)) {
            cell.select();
        }
    }, true);
}
