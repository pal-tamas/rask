// `.Confirm("…")` asked in the kit's dialog, Ui.Confirm, where the layout placed one: rask-confirm.ts asks through
// `seam.confirm`, which is answered here. Without the dialog it stays the browser's confirm() and prompt().
// A question asked from code (Ui.Confirm.Ask) comes through `seam.ask` (rask-confirm-ask.ts) and is asked here too.
//
// The dialog is a Ui.Modal (rask-ask.ts opens it and hears how it closed). Its parts carry data-rask-confirm-dialog:
// `message` the question, `heading` its title (`heading:`, else the layout's), `field` the box `expect:` is typed into,
// `go` the button that says yes, `no` the one that says no; each keeps the layout's words in
// data-rask-confirm-default. Nothing of it is the page's to hear: opening,
// typing and closing send nothing, and only a yes lets the action go — made again by the press or the submit that
// asked, which the runtime then lets through; for a question from code, the answer is what goes back.
//
// What the render cannot know is held against it: the title, the labels, Yes's look, whether the field shows, and
// whether yes may be pressed.

import {dialogAsking} from "./rask-ask.js";
import {disown, listen, near, own, page, seam, type AskWords} from "./rask-owned.js";

const PART = "data-rask-confirm-dialog";
const FIELD = "[" + PART + "=field]";
// The look Yes is drawn in, and the other one's classes, for a question that asks in that one.
const TONE = "data-rask-confirm-tone";
const OTHER = "data-rask-confirm-other";
const DEFAULT = "data-rask-confirm-default";

if (page) {
    const doc = page;
    const ask = dialogAsking(PART);
    // What the reader has said yes to while it is made again: a form and its button may both ask.
    let passed: Element[] = [];
    // The answer `expect:` wants while the dialog asks for one.
    let expected: string | null = null;
    // The question's own title (`heading:`), or null for the layout's.
    let heading: string | null = null;
    // The words and the tone a question from code brought; null for the layout's.
    let words: AskWords | null = null;
    // Yes's own classes while it wears the other tone's, to be put back.
    let drawn: {go: HTMLElement; className: string} | null = null;

    const part = function <T extends HTMLElement>(dialog: Element, name: string): T | null {
        return dialog.querySelector<T>("[" + PART + "=" + name + "]");
    };

    // No holds its words; Yes (`go`) holds Flux's spinner and its words in the span after it.
    const label = function (dialog: Element, name: string, text: string | null): void {
        const el = part(dialog, name === "yes" ? "go" : name);
        const into = el && el.hasAttribute(DEFAULT) ? (name === "yes" ? el.querySelector(":scope > span") : el) : null;
        if (into) into.textContent = text ?? el!.getAttribute(DEFAULT) ?? "";
    };

    // Yes in the tone asked for: the dialog's own unless a question from code brought the other.
    const tone = function (go: HTMLElement, wanted: string | null): void {
        const other = go.getAttribute(OTHER);
        if (wanted === null || wanted === go.getAttribute(TONE) || other === null) return;
        drawn = {go: go, className: go.className};
        own(go, "class");
        go.className = other;
    };

    const untone = function (): void {
        if (!drawn) return;
        drawn.go.className = drawn.className;
        disown(drawn.go, "class");
        drawn = null;
    };

    // Before it opens: the question's title and labels, the field shown, empty, only when an answer is wanted, and
    // yes held until it is typed.
    const settle = function (dialog: Element): void {
        const field = part(dialog, "field");
        const input = field ? field.querySelector("input") : null;
        const go = part<HTMLButtonElement>(dialog, "go");
        const title = part(dialog, "heading");
        if (title) title.textContent = heading ?? title.getAttribute(DEFAULT) ?? "";
        label(dialog, "no", words ? words.no : null);
        label(dialog, "yes", words ? words.yes : null);
        if (field) {
            own(field, "hidden");
            field.hidden = expected === null;
        }
        if (input) input.value = "";
        if (go) {
            own(go, "disabled");
            go.disabled = expected !== null;
            tone(go, words ? words.tone : null);
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
        words = null;
        heading = el.getAttribute("data-rask-confirm-heading");
        const dialog = ask(question, yes, done, settle);
        if (!dialog) {
            expected = null;
            return null;
        }
        const input = expect === null ? null : dialog.querySelector<HTMLInputElement>(FIELD + " input");
        if (input) input.focus();
        return false;
    };

    // One question from code at a time: the next waits for the one before it to be answered.
    let turn: Promise<unknown> = Promise.resolve();

    // Asks `question` once the dialog is free: true on yes, false on every other way it closes, null with no dialog.
    const askNow = function (question: string, said: AskWords): Promise<boolean | null> {
        return new Promise(function (resolve) {
            const message = doc.querySelector("[" + PART + "=message]");
            const open = message ? message.closest("dialog") : null;
            if (!open) {
                resolve(null);
                return;
            }
            const begin = function (): void {
                // Where focus goes back to: the control whose handler asked, as it is on the page now.
                const back = doc.activeElement;
                const done = function (): void {
                    words = heading = null;
                    untone();
                    if (back instanceof HTMLElement && back.isConnected) back.focus({preventScroll: true});
                    // After the yes, which runs straight after this when it was one.
                    void Promise.resolve().then(function () { resolve(false); });
                };
                expected = null;
                words = said;
                heading = said.heading;
                if (!ask(question, function () { resolve(true); }, done, settle)) {
                    words = heading = null;
                    resolve(null);
                }
            };
            // Asking already — a press's `.Confirm`: this question opens once that one has closed.
            if (open.open) open.addEventListener("close", function () { setTimeout(begin, 0); }, {once: true});
            else begin();
        });
    };

    seam.ask = function (question, said) {
        const mine = turn.then(function () { return askNow(question, said); });
        turn = mine;
        return mine;
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
