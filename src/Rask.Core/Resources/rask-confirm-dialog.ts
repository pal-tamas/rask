// `.Confirm("…")` asked in the kit's dialog, Ui.Confirm, where the layout placed one: rask-confirm.ts asks through
// `seam.confirm`, which is answered here. Without the dialog it stays the browser's confirm() and prompt().
//
// The dialog is a Ui.Modal (rask-ask.ts opens it and hears how it closed). Its parts carry data-rask-confirm-dialog:
// `message` the question, `field` the box `expect:` is typed into, `go` the button that says yes. Nothing of it is
// the page's to hear: opening, typing and closing send nothing, and only a yes lets the action go — made again by
// the press or the submit that asked, which the runtime then lets through.
//
// What the render cannot know is held against it: whether the field shows, and whether yes may be pressed yet.

import {dialogAsking} from "./rask-ask.js";
import {listen, near, own, page, seam} from "./rask-owned.js";

const PART = "data-rask-confirm-dialog";
const FIELD = "[" + PART + "=field]";

if (page) {
    const doc = page;
    const ask = dialogAsking(PART);
    // What the reader has said yes to while it is made again: a form and its button may both ask.
    let passed: Element[] = [];
    // The answer `expect:` wants while the dialog asks for one.
    let expected: string | null = null;

    const part = function <T extends HTMLElement>(dialog: Element, name: string): T | null {
        return dialog.querySelector<T>("[" + PART + "=" + name + "]");
    };

    // Before it opens: the field shown, empty, only when an answer is wanted, and yes held until it is typed.
    const settle = function (dialog: Element): void {
        const field = part(dialog, "field");
        const input = field ? field.querySelector("input") : null;
        const go = part<HTMLButtonElement>(dialog, "go");
        if (field) {
            own(field, "hidden");
            field.hidden = expected === null;
        }
        if (input) input.value = "";
        if (go) {
            own(go, "disabled");
            go.disabled = expected !== null;
        }
    };

    seam.confirm = function (el, question, expect, again) {
        if (passed.indexOf(el) >= 0) return true;
        const before = passed;
        // The control that was pressed — for a form, whatever had focus: its button, or the field Enter was pressed in.
        const back = el.tagName === "FORM" ? doc.activeElement : el;
        const yes = function (): void {
            passed = before.concat(el);
            try {
                again();
            } finally {
                passed = [];
            }
        };
        const done = function (): void {
            expected = null;
            if (back instanceof HTMLElement && back.isConnected) back.focus({preventScroll: true});
        };
        expected = expect;
        const dialog = ask(question, yes, done, settle);
        if (!dialog) {
            expected = null;
            return null;
        }
        const input = expect === null ? null : dialog.querySelector<HTMLInputElement>(FIELD + " input");
        if (input) input.focus();
        return false;
    };

    // Yes, only on the answer typed exactly — case and all, as prompt() is matched.
    listen("input", function (e) {
        const field = near(e.target, FIELD);
        const dialog = field ? field.closest("dialog") : null;
        const go = dialog ? part<HTMLButtonElement>(dialog, "go") : null;
        if (go && expected !== null) go.disabled = (e.target as HTMLInputElement).value !== expected;
    });
    // Enter in the field is yes, once yes may be pressed.
    listen("keydown", function (e) {
        const field = e.key === "Enter" ? near(e.target, FIELD) : null;
        const dialog = field ? field.closest("dialog") : null;
        const go = dialog ? part<HTMLButtonElement>(dialog, "go") : null;
        if (go) {
            e.preventDefault();
            if (!go.disabled) go.click();
        }
    });
}
