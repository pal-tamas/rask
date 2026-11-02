// A pick that waits for the next action, held in the browser: a list that says `held` (rask-list.ts).
//
// Flux's select and pillbox under a plain `wire:model` send nothing when an option is picked: they stamp the
// option into the button, or a pill for it, and Livewire carries the value with the next action (measured on
// fluxui.dev/components/select and /pillbox, 2026-11-02). A bound list that waits does the same. Its rows carry
// `data-rask-list-value` (the form value they post) and no handler; a pick, a pill's cross
// (`data-rask-list-remove`) or the clear button (`data-rask-list-clear`) is written here into the list's ONE bound
// field (rask-bound.ts) — a JSON array of form values, in the order they were picked — and announced there with
// `input` and `change`. That field says `data-rask-bind-on="action"`, so the runtime keeps it and sends it ahead
// of the reader's next action (rask-input.ts); a form that asks before it is left counts it (rask-leave.ts). A
// list of several answers (`multiple`) switches the row picked; a list of one takes it.
//
// WHAT IS DRAWN. The rows' `aria-selected` and `data-selected`, the trigger's `data-selected`, the clear button
// shown or hidden and a named list's posted field — each an attribute the page renders too. And what the trigger
// says: the boxes that say it are drawn by the page once and are the browser's from then on
// (`data-rask-opaque`: no diff and no morph goes into them), stamped from the list's template
// (`data-rask-list-faces`):
//   data-rask-list-shows   a button's words: the placeholder; the answer the page last drew, as it drew it
//                          (`data-value`); the picked row's content copied into the box `data-rask-list-face`
//                          names the class of — as Flux copies its option; or, for several, the count
//                          (`data-rask-list-count`: the words after it);
//   data-rask-list-pills   a pillbox's pills, in the order they were picked: the pill the page last drew for an
//                          answer, or the template's blank pill (`data-rask-list-pill`) given its row's words;
//   data-rask-list-blank   a pillbox's placeholder while it holds nothing — an input among the pills says it as
//                          its own `placeholder` instead;
//   data-rask-list-posts   a named list of several answers: one posted field per answer, named `data-name`.
// When the page writes an answer of its own, the field's `value` moves and the boxes follow it. A combobox's
// input says its pick through rask-list.ts (`strict`).

import {announce, bind, boundOf} from "./rask-bound.js";

const HELD = "[data-rask-list~=held]";
const VALUE = "data-rask-list-value";
const PLACEHOLDER = "[data-ui-select-placeholder],[data-ui-pillbox-placeholder]";
/** The clear button of a held list. */
export const CLEAR = "data-rask-list-clear";

interface Held {
    /** The answers the list shows, as form values. */
    shown: string[];
    /** The bound field as the page last rendered it. */
    drawn: string;
}

const helds = new WeakMap<Element, Held>();

function read(field: string): string[] {
    try {
        const values: unknown = JSON.parse(field);
        return Array.isArray(values) ? values.filter(function (value): value is string { return typeof value === "string"; }) : [];
    } catch (e) {
        return [];
    }
}

function same(a: readonly string[], b: readonly string[]): boolean {
    return a.length === b.length && a.every(function (value, i) { return value === b[i]; });
}

function heldOf(root: Element): Held {
    let held = helds.get(root);
    if (!held) {
        const bound = boundOf(root);
        const drawn = (bound && bound.getAttribute("value")) || "[]";
        helds.set(root, held = {shown: read(drawn), drawn});
    }
    return held;
}

// The field's value for what the list shows: as the page wrote it while that is what it says, so a late echo is
// known for one (rask-bound.ts) however the page escaped it.
function current(root: Element): string {
    const held = heldOf(root);
    return same(held.shown, read(held.drawn)) ? held.drawn : JSON.stringify(held.shown);
}

// The first of `els` that says `value` in `name`.
function find<E extends Element>(els: ArrayLike<E>, name: string, value: string): E | null {
    for (let i = 0; i < els.length; i++) {
        if (els[i].getAttribute(name) === value) {
            return els[i];
        }
    }
    return null;
}

function toggle(el: Element, name: string, on: boolean): void {
    if (on) {
        el.setAttribute(name, "");
    } else {
        el.removeAttribute(name);
    }
}

/** The rows say which of them is picked, as the page draws them. Asked again whenever a render redrew the rows. */
export function markHeld(root: Element): void {
    const shown = heldOf(root).shown;
    const rows = root.querySelectorAll("[" + VALUE + "]");
    for (let i = 0; i < rows.length; i++) {
        const picked = shown.indexOf(rows[i].getAttribute(VALUE)!) >= 0;
        if (rows[i].getAttribute("aria-disabled") !== "true") {
            rows[i].setAttribute("aria-selected", picked ? "true" : "false");
        }
        toggle(rows[i], "data-selected", picked);
    }
}

// What the button says for `shown`: a count of several, the answer as the page drew it, the picked row copied, or
// the placeholder — which is also what the page says for an answer it has nothing to show of.
function face(root: Element, faces: HTMLTemplateElement, shown: readonly string[]): Node | null {
    const count = shown.length > 1 ? faces.content.querySelector("[data-rask-list-count]") : null;
    if (count) {
        const counted = count.cloneNode(false) as Element;
        counted.removeAttribute("data-rask-list-count");
        counted.textContent = shown.length + " " + count.getAttribute("data-rask-list-count");
        return counted;
    }
    if (shown.length) {
        const drawn = find(faces.content.querySelectorAll("[data-value]"), "data-value", shown[0]);
        const copied = drawn ? drawn.cloneNode(true) : copy(root, faces, shown[0]);
        if (copied) {
            return copied;
        }
    }
    return placeholderOf(faces);
}

function placeholderOf(faces: HTMLTemplateElement): Node | null {
    const placeholder = faces.content.querySelector(PLACEHOLDER);
    return placeholder ? placeholder.cloneNode(true) : null;
}

// The picked row's content in the button's box, with the words it says there.
function copy(root: Element, faces: HTMLTemplateElement, value: string): Element | null {
    const box = faces.content.querySelector("[data-rask-list-face]");
    const row = find(root.querySelectorAll("[" + VALUE + "]"), VALUE, value);
    const content = row && row.firstElementChild;
    if (!box || !content) {
        return null;
    }
    const picked = box.cloneNode(false) as Element;
    picked.removeAttribute("data-rask-list-face");
    picked.setAttribute("data-value", value);
    const body = content.cloneNode(true) as Element;
    body.setAttribute("class", box.getAttribute("data-rask-list-face") || "");
    const says = row!.getAttribute("data-rask-list-says");
    const words = body.children[1] && body.children[1].firstElementChild;
    if (says !== null && words) {
        words.textContent = says;
    }
    picked.appendChild(body);
    return picked;
}

// A pillbox's pills for `shown`, in the wrapper the page draws them in: each as the page last drew it, or the
// blank pill given its row's words. A value with neither is no answer the list can show, and has no pill.
function pills(root: Element, faces: HTMLTemplateElement, shown: readonly string[]): Node | null {
    const drawn = faces.content.querySelector("[data-rask-list-pills]");
    const blank = drawn && drawn.querySelector("[data-rask-list-pill]");
    if (!drawn || !blank || !shown.length) {
        return null;
    }
    const wrapper = drawn.cloneNode(false) as Element;
    wrapper.removeAttribute("data-rask-list-pills");
    const rows = root.querySelectorAll("[" + VALUE + "]");
    for (const value of shown) {
        const kept = find(drawn.querySelectorAll("[data-value]"), "data-value", value);
        const row = kept ? null : find(rows, VALUE, value);
        if (kept || row) {
            const pill = (kept || blank).cloneNode(true) as Element;
            pill.removeAttribute("data-rask-list-pill");
            pill.setAttribute("data-value", value);
            if (row && pill.firstElementChild) {
                const says = row.getAttribute("data-rask-list-says");
                pill.firstElementChild.textContent = says !== null ? says : (row.textContent || "").trim();
            }
            wrapper.appendChild(pill);
        }
    }
    return wrapper;
}

// A named list of several answers posts one field per answer, and one empty field for none.
function posts(box: Element, shown: readonly string[]): void {
    const name = box.getAttribute("data-name") || "";
    box.replaceChildren(...(shown.length ? shown : [""]).map(function (value) {
        const field = box.ownerDocument.createElement("input");
        field.type = "hidden";
        field.name = name;
        field.value = value;
        return field;
    }));
}

function fill(box: Element | null, content: Node | null): void {
    if (box) {
        box.replaceChildren(...(content ? [content] : []));
    }
}

// Everything the list shows of its answers, from what it holds now.
function draw(root: Element): void {
    const held = heldOf(root);
    const any = held.shown.length > 0;
    markHeld(root);
    const trigger = root.querySelector("[aria-haspopup=listbox]");
    if (trigger) {
        toggle(trigger, "data-selected", any);
    }
    root.querySelectorAll("[" + CLEAR + "]").forEach(function (clear) {
        clear.classList.replace(any ? "hidden" : "flex", any ? "flex" : "hidden");
    });
    const bound = boundOf(root);
    const box = root.querySelector("[data-rask-list-posts]");
    if (box) {
        posts(box, held.shown);
    } else {
        root.querySelectorAll<HTMLInputElement>("input[type=hidden][name]").forEach(function (posted) {
            if (posted !== bound) {
                posted.value = any ? held.shown[0] : "";
            }
        });
    }
    const faces = root.querySelector<HTMLTemplateElement>("template[data-rask-list-faces]");
    if (!faces) {
        return;
    }
    fill(root.querySelector("[data-rask-list-shows]"), face(root, faces, held.shown));
    const pillBox = root.querySelector("[data-rask-list-pills]");
    if (pillBox) {
        fill(pillBox, pills(root, faces, held.shown));
        fill(root.querySelector("[data-rask-list-blank]"), any ? null : placeholderOf(faces));
        const input = root.querySelector<HTMLInputElement>("[data-ui-pillbox-trigger] input");
        const placeholder = faces.content.querySelector(PLACEHOLDER);
        if (input && placeholder) {
            input.placeholder = any ? "" : placeholder.textContent || "";
        }
    }
}

function hold(root: Element, values: string[]): void {
    heldOf(root).shown = values;
    draw(root);
    announce(root, current(root));
}

/**
 * A row of a held list was picked: the answer, or — among several — switched. One that posts no value is the
 * page's own (a create row), and is left to it.
 */
export function pickHeld(root: Element, row: Element): void {
    const value = row.getAttribute(VALUE);
    if (value === null) {
        return;
    }
    const shown = heldOf(root).shown;
    if (!root.matches("[data-rask-list~=multiple]")) {
        hold(root, [value]);
    } else {
        hold(root, shown.indexOf(value) >= 0 ? shown.filter(function (held) { return held !== value; }) : shown.concat(value));
    }
}

/** A pill's cross was pressed: its answer goes. */
export function removeHeld(root: Element, cross: Element): void {
    const pill = cross.closest("[data-value]");
    const value = pill ? pill.getAttribute("data-value") : null;
    if (value !== null) {
        hold(root, heldOf(root).shown.filter(function (held) { return held !== value; }));
    }
}

/** The clear button of a held list was pressed: it holds nothing. */
export function clearHeld(root: Element): void {
    hold(root, []);
}

bind(HELD, current, function (root, rendered) {
    // The page wrote an answer of its own, or the list just arrived with one.
    const held = heldOf(root);
    held.drawn = rendered || "[]";
    held.shown = read(held.drawn);
    draw(root);
}, undefined, function (root, rendered) {
    // The page has heard what the reader picked: what it draws around the field says so too.
    heldOf(root).drawn = rendered || "[]";
    draw(root);
});
