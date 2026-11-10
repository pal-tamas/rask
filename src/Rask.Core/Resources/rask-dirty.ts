// A form the reader changed says so, at once and with no round trip: `data-dirty` on the <form> and on each field
// whose value is not the one it had (Livewire's `wire:dirty`, written as an attribute so Tailwind's `data-dirty:`
// styles it).
//
// WHICH FORMS. Every form the page handles a submit of (`data-rask-on-submit`). A field that holds none of the
// form's data — a list's search box — says `data-rask-unguarded` and is never marked; nor is a value the runtime
// wrote by itself (`unasked`, rask-owned.ts).
//
// A DIFFERENCE, NOT A HISTORY. A field remembers the value it had the first time the reader changed it (what the
// render last wrote: `defaultValue`, `defaultChecked`, `defaultSelected`), and is dirty while it holds another.
// Typed and typed back, it is clean again. A checkbox by whether it is checked, a select by what is selected, a
// radio with the rest of its group, a file input by whether it holds files.
//
// WHAT ONLY THE PAGE HEARD. A control drawn in C# (a listbox, a calendar, a pillbox) changes on a press the browser
// raises no `input` for. The page says it on the form instead, `data-rask-edited`, while such a pick leaves a field
// other than it was (HTMLFormElement): the form is dirty while it says so.
//
// SAVED, OR PUT BACK. The form's render changes `data-rask-saved` after every submit it accepted, and
// `data-rask-settled` after `EditContext.Reset()` or `EditContext.MarkSaved()`. Then what the render wrote into each field is its value from now
// on: a field still holding something else — typed while the save was on its way — stays dirty, and the rest are
// clean. A refused submit changes neither, and every mark stays.
//
// The marks are the runtime's, not a render's (rask-owned.ts `own`): a render of the form must not take them away.

import {disown, isUnasked, near, own, page} from "./rask-owned.js";

const FORM = "form[data-rask-on-submit]";
const UNGUARDED = "[data-rask-unguarded]";
const DIRTY = "data-dirty";
const EDITED = "data-rask-edited";
const SAVED = "data-rask-saved";
const SETTLED = "data-rask-settled";

type Field = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

/** What each field held before the reader first changed it, as `valueOf` reads it. */
const originals = new WeakMap<Element, string>();

/** The fields of each form that hold a value other than their original. */
const dirtyFields = new WeakMap<Element, Set<Element>>();

/** The forms a pick reached through a hidden input since they were last saved or put back. */
const picked = new WeakSet<Element>();

// A hidden input carries the pick of a control drawn in C# (a pillbox, a listbox, a calendar writes it in the
// browser): its value and its rendered value are one attribute, so there is no original to compare it with. Its
// change marks the FORM, as a pick only the page heard does, until a save or a reset; the input itself is no field
// a reader sees, and is never marked.
function isCarrier(el: Element | null): boolean {
    return !!el && el.tagName === "INPUT" && (el as HTMLInputElement).type === "hidden";
}

function isField(el: Element | null): el is Field {
    return !!el && (el.tagName === "SELECT" || el.tagName === "TEXTAREA"
        || (el.tagName === "INPUT" && (el as HTMLInputElement).type !== "hidden"));
}

function isToggle(el: Field): el is HTMLInputElement {
    return el.tagName === "INPUT" && ((el as HTMLInputElement).type === "checkbox" || (el as HTMLInputElement).type === "radio");
}

function selected(select: HTMLSelectElement, rendered: boolean): string {
    const picked: string[] = [];
    const options = select.options;
    for (let i = 0; i < options.length; i++) {
        if (rendered ? options[i].defaultSelected : options[i].selected) picked.push(options[i].value);
    }
    // A single select that the render marked nothing selected in shows its first option.
    if (rendered && picked.length === 0 && !select.multiple && options.length > 0) picked.push(options[0].value);
    return picked.join("\n");
}

/** The field's value as the reader sees it now. */
export function valueOf(el: Field): string {
    if (isToggle(el)) return el.checked ? "1" : "0";
    if (el.tagName === "SELECT") return selected(el as HTMLSelectElement, false);
    if (el.tagName === "INPUT" && (el as HTMLInputElement).type === "file") {
        const files = (el as HTMLInputElement).files;
        return files && files.length > 0 ? String(files.length) : "";
    }
    return el.value;
}

/** The field's value as the render last wrote it. */
export function renderedOf(el: Field): string {
    if (isToggle(el)) return el.defaultChecked ? "1" : "0";
    if (el.tagName === "SELECT") return selected(el as HTMLSelectElement, true);
    if (el.tagName === "INPUT" && (el as HTMLInputElement).type === "file") return "";
    return (el as HTMLInputElement | HTMLTextAreaElement).defaultValue;
}

function mark(el: Element, on: boolean): void {
    if (on === el.hasAttribute(DIRTY)) return;
    if (on) {
        own(el, DIRTY);
        el.setAttribute(DIRTY, "");
    } else {
        el.removeAttribute(DIRTY);
        disown(el, DIRTY);
    }
}

function fieldsOf(form: Element): Set<Element> {
    let fields = dirtyFields.get(form);
    if (!fields) dirtyFields.set(form, fields = new Set());
    return fields;
}

/** Whether `form` holds anything the reader changed: a field of its own, or a pick only the page heard. */
export function isDirty(form: Element): boolean {
    const fields = dirtyFields.get(form);
    return (!!fields && fields.size > 0) || picked.has(form) || form.hasAttribute(EDITED);
}

function markForm(form: Element): void {
    mark(form, isDirty(form));
}

// Compares one field with its original and marks it.
function judge(form: Element, el: Field): void {
    const fields = fieldsOf(form);
    const dirty = el.isConnected !== false && valueOf(el) !== originals.get(el);
    if (dirty) fields.add(el); else fields.delete(el);
    mark(el, dirty);
}

// The fields one change can move: a radio takes the rest of its group with it.
function moved(form: Element, el: Field): Field[] {
    if (el.tagName !== "INPUT" || (el as HTMLInputElement).type !== "radio" || !el.name) return [el];
    const group: Field[] = [];
    const all = form.querySelectorAll("input[type=radio]");
    for (let i = 0; i < all.length; i++) {
        if ((all[i] as HTMLInputElement).name === el.name) group.push(all[i] as HTMLInputElement);
    }
    return group;
}

/** Hears a reader's `input` or `change` on `target`. */
export function changed(target: EventTarget | null): void {
    const el = target instanceof Element ? target : null;
    if (!el || isUnasked() || near(el, UNGUARDED)) return;
    const form = near(el, FORM);
    if (!form) return;
    if (isCarrier(el)) {
        picked.add(form);
        markForm(form);
        return;
    }
    if (!isField(el)) return;
    for (const field of moved(form, el)) {
        if (!originals.has(field)) originals.set(field, renderedOf(field));
        judge(form, field);
    }
    markForm(form);
}

/** The render says `form` was saved or put back: what it wrote into each field is that field's value now. */
export function rebase(form: Element): void {
    picked.delete(form);
    const fields = dirtyFields.get(form);
    if (fields) {
        for (const el of Array.from(fields)) {
            if (!isField(el)) continue;
            originals.set(el, renderedOf(el));
            judge(form, el);
        }
    }
    markForm(form);
}

/** What the render wrote changed on `form`: a save, a reset, or a pick only the page heard. */
export function rendered(form: Element, name: string): void {
    if (name === SAVED || name === SETTLED) rebase(form); else markForm(form);
}

if (page) {
    const doc = page;
    const heard = function (e: Event): void { changed(e.target); };
    // On the way down: a control that stops its own event from bubbling has still been changed.
    doc.addEventListener("input", heard, true);
    doc.addEventListener("change", heard, true);
    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            for (const record of records) {
                const target = record.target as Element;
                if (record.attributeName && target.matches && target.matches(FORM)) rendered(target, record.attributeName);
            }
        }).observe(doc.documentElement, {subtree: true, attributes: true, attributeFilter: [SAVED, SETTLED, EDITED]});
    }
}
