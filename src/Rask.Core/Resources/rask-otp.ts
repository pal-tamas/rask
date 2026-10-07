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
// the cells are refilled from it.
//
// data-rask-otp="numeric" (the default) | "alpha" | "alphanumeric" says what a cell accepts.

import {page} from "./rask-owned.js";

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

function announce(group: Element, cells: HTMLInputElement[]): void {
    const bound = group.querySelector<HTMLInputElement>("input[type=hidden]");
    const value = join(cells);
    if (!bound || bound.value === value) {
        return;
    }
    write(bound, value);
    let waiting = sent.get(bound);
    if (!waiting) {
        sent.set(bound, waiting = []);
    }
    waiting.push(value);
    bound.dispatchEvent(new Event("input", {bubbles: true}));
    bound.dispatchEvent(new Event("change", {bubbles: true}));
}

// Rewrites the whole code and puts the caret on cell `index`.
function commit(group: Element, cells: HTMLInputElement[], value: string, index: number): void {
    fill(cells, value);
    land(cells, index);
    announce(group, cells);
}

// The page's answer to each value announced comes back as the hidden field's `value` attribute — and it comes
// back LATE: the echo of "12" arrives when the cells already say "1234". An echo is therefore recognised (it
// is one of the values still waiting) and ignored, and the field is put back to what the cells say. Anything
// else in that attribute is the page changing the code, and the cells follow it.
const sent = new WeakMap<Element, string[]>();

// A hidden input's `value` IS its attribute, so writing it here looks, to the observer below, exactly like the
// page writing it. The records this write caused are taken back out before the observer is handed them.
let watcher: MutationObserver | null = null;

function write(bound: HTMLInputElement, value: string): void {
    if (bound.value === value) {
        return;
    }
    // What the page wrote and the observer has not been handed yet is sorted out FIRST, while the attribute
    // still says what the page wrote. A record names the attribute, not its value: read after the write
    // below, a late echo of "1" would look like the page setting the code to what was just typed — every
    // echo still waiting would be forgotten, and the next one to arrive would take the cells back.
    if (watcher) {
        observed(watcher.takeRecords());
    }
    bound.value = value;
    if (watcher) {
        watcher.takeRecords();
    }
}

// ONCE per group, however many records name it. A record says the attribute changed, not what it changed to,
// and one render writes a field twice (the attribute, then the property, which on a hidden input is the same
// attribute): the second record would be read after the first one's answer — the cells' own value, put back
// over the echo — and that value is waiting too, so it would pass for ITS echo, and every echo before it be
// forgotten. The next one to arrive would then take the cells back.
function observed(records: MutationRecord[]): void {
    const seen = new Set<Element>();
    const once = function (group: Element): void {
        if (!seen.has(group)) {
            seen.add(group);
            refill(group);
        }
    };
    for (const record of records) {
        if (record.type === "attributes") {
            const t = record.target as Element;
            const group = t instanceof HTMLInputElement && t.type === "hidden" ? t.closest(GROUP) : null;
            if (group) once(group);
            continue;
        }
        record.addedNodes.forEach(function (n) {
            if (n instanceof Element) {
                if (n.matches(GROUP)) once(n);
                n.querySelectorAll(GROUP).forEach(once);
            }
        });
    }
}

function refill(group: Element): void {
    const bound = group.querySelector<HTMLInputElement>("input[type=hidden]");
    if (!bound) {
        return;
    }
    const cells = cellsOf(group);
    const rendered = bound.getAttribute("value") || "";
    const waiting = sent.get(bound);
    const echo = waiting ? waiting.indexOf(rendered) : -1;
    if (waiting && echo >= 0) {
        waiting.splice(0, echo + 1);
        write(bound, join(cells));
        return;
    }
    if (waiting) {
        waiting.length = 0;
    }
    if (join(cells) !== rendered) {
        fill(cells, otpChars(rendered, group.getAttribute("data-rask-otp"), cells.length));
    }
    write(bound, join(cells));
}

if (page) {
    const doc = page;

    doc.addEventListener("beforeinput", function (e) {
        const cell = e.target;
        const group = cell instanceof HTMLInputElement ? cell.closest(GROUP) : null;
        const data = (e as InputEvent).data;
        // One character that does not fit is refused before it shows. Longer text (a paste, an autofilled
        // code) is sorted out once it has arrived.
        if (group && data && data.length === 1 && !accepts(group, data)) {
            e.preventDefault();
        }
    }, true);

    doc.addEventListener("input", function (e) {
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

    doc.addEventListener("keydown", function (e) {
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

    doc.addEventListener("paste", function (e) {
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
    doc.addEventListener("focusin", function (e) {
        const cell = e.target;
        const group = cell instanceof HTMLInputElement && cell.type !== "hidden" ? cell.closest(GROUP) : null;
        if (group) {
            const cells = cellsOf(group);
            land(cells, cells.indexOf(cell as HTMLInputElement));
        }
    }, true);
    // The press that follows the focus would put a caret where it landed and drop the selection.
    doc.addEventListener("pointerup", function (e) {
        const cell = e.target;
        if (cell instanceof HTMLInputElement && cell === doc.activeElement && cell.closest(GROUP)) {
            cell.select();
        }
    }, true);

    if (typeof MutationObserver === "function") {
        // The page changed the code: its hidden field's `value` attribute moved, or the group just arrived.
        watcher = new MutationObserver(observed);
        watcher.observe(doc.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: ["value"]});
        doc.querySelectorAll(GROUP).forEach(refill);
    }
}
