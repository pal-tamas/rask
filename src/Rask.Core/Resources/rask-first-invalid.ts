// A submit the page refused takes the reader to what is wrong: focus goes to the first invalid field
// (`aria-invalid="true"`, in document order) and it is scrolled into view. Both hosts ask here, once the render
// that answers a submit is on the page.
//
// WHAT COUNTS AS REFUSED. Not only the browser's own constraint check, which already focuses its field and
// raises no submit: the form's rules failing on the server, and a save that refused with field failures. The
// form says so itself — `data-rask-refused` holds how many submits it has refused (HTMLFormElement.cs) — so a
// submit whose answer changed it was refused, and one that did not was not, whatever is still marked.
//
// WHICH FIELD. The first one marked that can take focus now: on screen, enabled, not in an inert part. A control
// of the kit's whose mark is on a wrapper hands focus to the first focusable thing inside it. A field on a tab
// that is not shown is passed over — `Ui.FormTabs` opens the tab holding the first one, and that tab is shown by
// rask-tabs.ts's observer after the render, so the field is looked for in the next task, not in this one.
//
// THE READER'S FOCUS. Moved elsewhere while the submit was in flight — into another field, a menu — it stays.
// Only a focus still where the press left it, or lost to the page while the form was held, is moved.

import {unasked} from "./rask-owned.js";

const REFUSED = "data-rask-refused";
const INVALID = "[aria-invalid=\"true\"]";
const FOCUSABLE = "input:not([type=hidden]),select,textarea,button,a[href],[tabindex],[contenteditable]";

/** One submit, as it was when it was pressed. */
export interface SubmitWatch {
    readonly form: Element;
    readonly refused: string | null;
    readonly focused: Element | null;
}

/** Takes note of a submit as it is pressed: what the form had refused, and where focus was. */
export function watchSubmit(form: Element): SubmitWatch {
    return {form, refused: form.getAttribute(REFUSED), focused: document.activeElement};
}

function shown(el: HTMLElement): boolean {
    return !(el as HTMLInputElement).disabled && !el.closest("[inert]") && el.getClientRects().length > 0;
}

function focusable(el: HTMLElement): HTMLElement | null {
    if (el.matches(FOCUSABLE) && shown(el)) return el;
    for (const inner of el.querySelectorAll<HTMLElement>(FOCUSABLE)) {
        if (shown(inner)) return inner;
    }
    return null;
}

/** The first field marked invalid in `form` that can take focus now, in document order. */
export function firstInvalid(form: Element): HTMLElement | null {
    for (const el of form.querySelectorAll<HTMLElement>(INVALID)) {
        const target = focusable(el);
        if (target) return target;
    }
    return null;
}

function movedOn(watch: SubmitWatch): boolean {
    const now = document.activeElement;
    return !!now && now !== document.body && now !== document.documentElement && now !== watch.focused;
}

function reducedMotion(): boolean {
    return typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/** Focuses and shows the first invalid field when the submit was refused; true when it did. */
export function focusFirstInvalid(watch: SubmitWatch): boolean {
    const form = watch.form;
    if (!form.isConnected || form.getAttribute(REFUSED) === watch.refused || movedOn(watch)) return false;
    const target = firstInvalid(form);
    if (!target) return false;
    // The runtime's own doing, not the reader's (rask-owned.ts): the focus it raises is no action to send ahead of.
    unasked(function () { target.focus({preventScroll: true}); });
    target.scrollIntoView({block: "center", behavior: reducedMotion() ? "auto" : "smooth"});
    return document.activeElement === target;
}

/** The render answering the submit is on the page: looked at in the next task, once a refused tab is shown. */
export function submitAnswered(watch: SubmitWatch): void {
    if (watch.form.getAttribute(REFUSED) === watch.refused) return;
    setTimeout(function () { focusFirstInvalid(watch); }, 0);
}
