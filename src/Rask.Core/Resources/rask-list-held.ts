// A pick that waits for the next action, held in the browser: a list that says `held` (rask-list.ts).
//
// Flux's select under a plain `wire:model` sends nothing when an option is picked: it stamps the option into its
// button and Livewire carries the value with the next action (measured on fluxui.dev/components/select,
// 2026-11-02). A bound select that waits does the same. Its rows carry `data-rask-list-value` (the form value
// they post) and no handler; a pick, or the clear button (`data-rask-list-clear`), is written here into the
// list's ONE bound field (rask-bound.ts) — a JSON array of form values — and announced there with `input` and
// `change`. That field says `data-rask-bind-on="action"`, so the runtime keeps it and sends it ahead of the
// reader's next action (rask-input.ts); a form that asks before it is left counts it (rask-leave.ts).
//
// WHAT IS DRAWN. The rows' `aria-selected` and `data-selected`, the trigger's `data-selected`, the clear button
// shown or hidden, a named list's posted field — each a write of an attribute the page renders too. And the
// button's words (`data-rask-list-shows`): the page's own content there is put out of sight and a stand-in
// stamped beside it from the list's template (`data-rask-list-faces`): its placeholder, or the picked row's
// content copied into the box `data-rask-list-face` names the class of — as Flux copies its option. A
// stand-in says `data-rask-managed`, so no diff counts it (rask-dom-path.ts): what the page renders keeps its
// place, and once a render says the answer itself — the field's `value` comes back — the stand-ins go and the
// page's content shows again. A combobox's input says its pick through rask-list.ts (`strict`).

import {announce, bind, boundOf} from "./rask-bound.js";

const HELD = "[data-rask-list~=held]";
const VALUE = "data-rask-list-value";
const DRAWN = "data-rask-list-drawn";
/** The clear button of a held list. */
export const CLEAR = "data-rask-list-clear";

interface Held {
    /** The answers the list shows, as form values. */
    shown: string[];
    /** The bound field as the page last rendered it. */
    drawn: string;
    /** The page's own elements put out of sight while a stand-in says something else. */
    hidden: HTMLElement[];
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
        helds.set(root, held = {shown: read(drawn), drawn, hidden: []});
    }
    return held;
}

// The field's value for what the list shows: as the page wrote it while that is what it says, so a late echo is
// known for one (rask-bound.ts) however the page escaped it.
function current(root: Element): string {
    const held = heldOf(root);
    return same(held.shown, read(held.drawn)) ? held.drawn : JSON.stringify(held.shown);
}

function rowOf(root: Element, value: string): HTMLElement | null {
    const rows = root.querySelectorAll<HTMLElement>("[" + VALUE + "]");
    for (let i = 0; i < rows.length; i++) {
        if (rows[i].getAttribute(VALUE) === value) {
            return rows[i];
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

// The page's words in the button out of sight, and the stand-in for what is shown now beside them.
function stamp(root: Element, held: Held): void {
    const shows = root.querySelector("[data-rask-list-shows]");
    const faces = root.querySelector<HTMLTemplateElement>("template[data-rask-list-faces]");
    if (!shows || !faces) {
        return;
    }
    const face = held.shown.length ? copy(root, faces, held.shown[0]) : placeholder(faces);
    for (let child = shows.firstElementChild; child; child = child.nextElementSibling) {
        const own = child as HTMLElement;
        own.style.display = "none";
        held.hidden.push(own);
    }
    if (face) {
        face.setAttribute(DRAWN, "");
        face.setAttribute("data-rask-managed", "");
        shows.appendChild(face);
    }
}

function placeholder(faces: HTMLTemplateElement): Element | null {
    const said = faces.content.querySelector("[data-ui-select-placeholder]");
    return said ? said.cloneNode(true) as Element : null;
}

// The picked row's content in the button's box, with the words it says there.
function copy(root: Element, faces: HTMLTemplateElement, value: string): Element | null {
    const box = faces.content.querySelector("[data-rask-list-face]");
    const row = rowOf(root, value);
    const content = row && row.firstElementChild;
    if (!box || !content) {
        return null;
    }
    const face = box.cloneNode(false) as Element;
    face.removeAttribute("data-rask-list-face");
    face.setAttribute("data-value", value);
    const body = content.cloneNode(true) as Element;
    body.setAttribute("class", box.getAttribute("data-rask-list-face") || "");
    const says = row!.getAttribute("data-rask-list-says");
    const words = body.children[1] && body.children[1].firstElementChild;
    if (says !== null && words) {
        words.textContent = says;
    }
    face.appendChild(body);
    return face;
}

// Everything the list shows of its answers, from what it holds now. What a render drew is the page's again first.
function draw(root: Element): void {
    const held = heldOf(root);
    root.querySelectorAll("[" + DRAWN + "]").forEach(function (el) { el.remove(); });
    for (const el of held.hidden) {
        el.style.removeProperty("display");
        if (el.getAttribute("style") === "") {
            el.removeAttribute("style");
        }
    }
    held.hidden = [];
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
    root.querySelectorAll<HTMLInputElement>("input[type=hidden][name]").forEach(function (posted) {
        if (posted !== bound) {
            posted.value = any ? held.shown[0] : "";
        }
    });
    if (!same(held.shown, read(held.drawn))) {
        stamp(root, held);
    }
}

function hold(root: Element, values: string[]): void {
    heldOf(root).shown = values;
    draw(root);
    announce(root, current(root));
}

/** A row of a held list was picked: true when it is one the browser picks, false for the page's own (create). */
export function pickHeld(root: Element, row: Element): boolean {
    const value = row.getAttribute(VALUE);
    if (value === null) {
        return false;
    }
    hold(root, [value]);
    return true;
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
    // The page has drawn what the reader picked: its own content says it now.
    heldOf(root).drawn = rendered || "[]";
    draw(root);
});
