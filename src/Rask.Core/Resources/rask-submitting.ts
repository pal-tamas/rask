// A form that is submitting takes no second submit.
//
// The loading mark (rask-loading.ts) waits 200 ms before it shows, and a press before then went through, so a
// fast double click on "Save" ran the handler twice: two uploads, two payouts. Livewire's rule for
// `wire:submit`, which this copies: "it will disable form submit buttons and mark all form inputs as readonly"
// while the submission is in flight, "so a user cannot submit the same form again until the initial
// submission is complete". At the press, in the same task, before anything is sent; the spinner keeps its
// delay, for the look only.
//
// What it writes, on the form's own controls (`form.elements`, so a `<button form="x">` outside it too):
//   disabled — on every submit button, so neither a second press nor Enter in a field submits again.
//   readonly — on every text-like input and textarea: what goes out is what was there at the press. A field a
//              render mid-flight asks to have corrected is handed back (handBack, below).
// Only where the render did not already write it, and each held against the morph (rask-owned.ts): the
// render the submit itself causes lands mid-flight and would otherwise hand the button back early. What a
// render says meanwhile is kept, and is what the control is left with when the submit is done.
//
// A submit that reaches a form already submitting — a script's requestSubmit, Enter in a form with no button —
// is dropped by the host, never sent. A plain button's click is not covered: Livewire disables nothing for
// `wire:click`, and neither does this.
//
// No DOM access at import: shared by both host bundles and imported by a Node fixture with a stub DOM.

import {LOADING_HARD_TIMEOUT_MS} from "./rask-loading.js";
import {disown, own} from "./rask-owned.js";

export const DISABLED = "disabled";
export const READONLY = "readonly";

/** One submit's hold on its form. Pair every one with {@link releaseForm}. */
export interface FormHold {
    readonly form: Element;
    /** What the hold wrote, and what the render last said for each: null for "not there". */
    readonly held: Map<Element, {name: string; rendered: string | null}>;
    /** The control that had focus at the press, given it back if disabling it threw focus to the page. */
    readonly focused: Element | null;
    hard: ReturnType<typeof setTimeout> | null;
    done: boolean;
}

const submitting = new WeakMap<Element, FormHold>();
const heldBy = new WeakMap<Element, FormHold>();
const live = new Set<FormHold>();

/** Whether `form` has a submit in flight: a second one is dropped. */
export function isSubmitting(form: Element | null): boolean {
    return !!form && submitting.has(form);
}

function submits(el: Element): boolean {
    const type = (el.getAttribute("type") || "").toLowerCase();
    return el.tagName === "BUTTON" ? type === "" || type === "submit" : type === "submit" || type === "image";
}

// What the reader could still change: readonly means nothing to a box, a list or a button, nor to `hidden`.
function typedInto(el: Element): boolean {
    if (el.tagName === "TEXTAREA") return true;
    if (el.tagName !== "INPUT") return false;
    const type = (el.getAttribute("type") || "").toLowerCase();
    return !/^(hidden|checkbox|radio|file|range|color|button|reset|submit|image)$/.test(type);
}

function controlsOf(form: Element): Element[] {
    const elements = (form as HTMLFormElement).elements;
    return elements ? Array.from(elements) : [];
}

/**
 * Marks `form` as submitting: its submit buttons disabled, its inputs readonly. Null when it already is — the
 * caller drops that submit.
 */
export function holdForm(form: Element): FormHold | null {
    if (submitting.has(form)) return null;
    const focused = typeof document !== "undefined" ? document.activeElement : null;
    const hold: FormHold = {form, held: new Map(), focused, hard: null, done: false};
    for (const el of controlsOf(form)) {
        const name = submits(el) ? DISABLED : typedInto(el) ? READONLY : null;
        if (!name || el.hasAttribute(name) || heldBy.has(el)) continue;
        el.setAttribute(name, "");
        own(el, name);
        heldBy.set(el, hold);
        hold.held.set(el, {name, rendered: null});
    }
    submitting.set(form, hold);
    live.add(hold);
    hold.hard = setTimeout(() => releaseForm(hold), LOADING_HARD_TIMEOUT_MS);
    return hold;
}

/** Ends a submit's hold: each control gets back what the render last said. Safe to call twice. */
export function releaseForm(hold: FormHold | null | undefined): void {
    if (!hold || hold.done) return;
    hold.done = true;
    live.delete(hold);
    if (hold.hard !== null) clearTimeout(hold.hard);
    if (submitting.get(hold.form) === hold) submitting.delete(hold.form);
    for (const [el, {name, rendered}] of hold.held) {
        heldBy.delete(el);
        disown(el, name);
        if (rendered === null) el.removeAttribute(name);
        else el.setAttribute(name, rendered);
    }
    const back = hold.focused as HTMLElement | null;
    if (back && hold.held.has(back) && back.isConnected && document.activeElement === document.body) back.focus();
}

/** Releases every form — the connection dropped, and nothing in flight will report back. */
export function releaseAllForms(): void {
    for (const hold of [...live]) releaseForm(hold);
}

// A FIELD THE PAGE ASKS TO HAVE CORRECTED IS THE READER'S AGAIN. A submit whose rules await — a lookup — draws
// what it already knows at that await: "Enter a whole number." under a field, the submit still in flight. That
// render listens for the first key of the correction (`data-rask-on-edit`, rask-input.ts), and a field still
// readonly would drop it. What the press sent went at the press, and the submit button stays disabled.
const ASKS_CORRECTION = "data-rask-on-edit";

function handBack(el: Element): void {
    const hold = heldBy.get(el);
    const slot = hold && hold.held.get(el);
    if (!hold || !slot || slot.name !== READONLY) return;
    hold.held.delete(el);
    heldBy.delete(el);
    disown(el, READONLY);
    if (slot.rendered === null) el.removeAttribute(READONLY);
    else el.setAttribute(READONLY, slot.rendered);
}

/** Called by the morph before it brings `from` up to `to`: what a render says of a held control is kept. */
export function noteRendered(from: Element, to: Element): void {
    const slot = heldBy.get(from)?.held.get(from);
    if (!slot) return;
    slot.rendered = to.getAttribute(slot.name);
    if (to.hasAttribute(ASKS_CORRECTION)) handBack(from);
}

/** Called by the diff for each attribute it sets: one that asks for a correction hands a held field back. */
export function noteAttributeSet(el: Element, name: string): void {
    if (name === ASKS_CORRECTION) handBack(el);
}
