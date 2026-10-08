// What a form field does in the browser, between the reader's key and the page's handler.
//
//   * data-rask-copy="<id>" on a button: a click copies that element's value (or its text) to the clipboard —
//     here, in the click's own call stack, because a clipboard write needs the gesture and a handler that ran
//     a round trip later no longer has it. The button then carries data-copied for 2 s (Flux UI's copyable
//     input, measured), which is what swaps its icon for a tick.
//   * data-rask-focus="<id>" focuses that element after a click; data-rask-clear="<id>" empties it first and
//     tells the page with a real `input` event. Clearing has to happen HERE for a bound field: the field takes
//     focus, and a render does not overwrite the value of the field the reader is in.
//   * data-rask-mask="(999) 999-9999" re-shapes what is typed — 9 a digit, a a letter, * either, anything else
//     itself — and data-rask-mask-money=".,2" groups a number (decimal mark, thousands mark, decimals). Both
//     run before the page's own input handler, so the page receives the shaped value, and both put the caret
//     back where the reader was typing.
//   * Enter toggles a checkbox that is a role="switch"; Shift+Arrow and PageUp/PageDown move a range input by
//     its data-rask-big-step. (A closed data-rask-listbox-button's keys are rask-keys.ts's.)
//
// Delegated to the document throughout. The shaping functions are exported for the Node fixture.

import {disown, listen, named, near, own, page} from "./rask-owned.js";

/** How long a button says it copied. Flux UI's input shows its tick for this long. */
export const COPIED_MS = 2000;

const RULES: Record<string, RegExp> = {"9": /[0-9]/, "a": /[a-zA-Z]/, "*": /[a-zA-Z0-9]/};

/**
 * Shapes `text` to `pattern`, dropping what does not fit. A literal is written only once a character
 * follows it, so "(999) 999" shows "(716" after three digits and "(716) 1" after four. `caret` is where the
 * caret sits in `text`; the result says where it belongs in the shaped value.
 */
export function maskPattern(text: string, pattern: string, caret: number = text.length): {value: string; caret: number} {
    let out = "";
    let pending = "";
    let at = -1;
    let i = 0;
    for (let p = 0; p < pattern.length; p++) {
        const rule = RULES[pattern[p]];
        if (!rule) {
            pending += pattern[p];
            if (text[i] === pattern[p]) {
                i++; // the reader typed the literal themselves
            }
            continue;
        }
        while (i < text.length && !rule.test(text[i])) {
            i++;
        }
        if (i >= text.length) {
            break;
        }
        if (at < 0 && i >= caret) {
            at = out.length;
        }
        out += pending + text[i++];
        pending = "";
    }
    return {value: out, caret: at < 0 ? out.length : at};
}

/**
 * Groups `text` as an amount: digits, one decimal mark, `precision` decimals, a leading minus. `marks` is
 * the decimal mark, the thousands mark and the number of decimals in one string — ".,2" when empty.
 */
export function maskMoney(text: string, marks: string, caret: number = text.length): {value: string; caret: number} {
    const decimal = marks[0] || ".";
    const thousands = marks.length > 1 ? marks[1] : ",";
    const precision = marks.length > 2 ? Number(marks.slice(2)) || 0 : 2;

    let whole = "";
    let fraction = "";
    let dotted = false;
    let kept = 0;      // the characters before the caret that survive: digits, the mark, the minus
    const negative = text.trim()[0] === "-";
    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        let keep = false;
        if (c >= "0" && c <= "9") {
            if (!dotted) {
                whole += c;
                keep = true;
            } else if (fraction.length < precision) {
                fraction += c;
                keep = true;
            }
        } else if (c === decimal && !dotted && precision > 0) {
            dotted = keep = true;
        } else if (c === "-" && negative && whole === "" && !dotted) {
            keep = true;
        }
        if (keep && i < caret) {
            kept++;
        }
    }
    whole = whole.replace(/^0+(?=\d)/, "");

    let value = negative ? "-" : "";
    for (let i = 0; i < whole.length; i++) {
        if (i > 0 && (whole.length - i) % 3 === 0) {
            value += thousands;
        }
        value += whole[i];
    }
    if (dotted) {
        value += decimal + fraction;
    }

    // The caret goes after as many surviving characters as were before it; the marks this added do not count.
    let at = 0;
    for (; at < value.length && kept > 0; at++) {
        if (value[at] !== thousands) {
            kept--;
        }
    }
    return {value, caret: caret >= text.length ? value.length : at};
}

type Field = HTMLInputElement | HTMLTextAreaElement;

function isField(el: Element | null): el is Field {
    return el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement;
}

function shape(field: Field, deleting: boolean): void {
    const pattern = field.getAttribute("data-rask-mask");
    const money = field.getAttribute("data-rask-mask-money");
    // A character deleted from a pattern is left deleted: re-shaping would pull the next one into its place
    // under the caret, which is how Flux's (Alpine's) mask behaves too. An amount is always regrouped.
    if (pattern === null && money === null || (deleting && pattern !== null)) {
        return;
    }
    let caret = field.value.length;
    try {
        caret = field.selectionStart ?? caret;
    } catch (e) {
        // a type with no selection (number, email)
    }
    const next = pattern !== null ? maskPattern(field.value, pattern, caret) : maskMoney(field.value, money!, caret);
    if (next.value === field.value) {
        return;
    }
    field.value = next.value;
    try {
        field.setSelectionRange(next.caret, next.caret);
    } catch (e) {
        // as above
    }
}

function fire(el: Element, type: string): void {
    el.dispatchEvent(new Event(type, {bubbles: true}));
}

const copied = new WeakMap<Element, number>();

function copy(button: HTMLElement): void {
    const source = named(button, "data-rask-copy");
    const clipboard = typeof navigator !== "undefined" ? navigator.clipboard : null;
    if (!source || !clipboard || typeof clipboard.writeText !== "function") {
        return;
    }
    const text = isField(source) || source instanceof HTMLSelectElement ? source.value : (source.textContent || "");
    clipboard.writeText(text).then(function () {
        own(button, "data-copied");
        button.setAttribute("data-copied", "");
        window.clearTimeout(copied.get(button));
        copied.set(button, window.setTimeout(function () {
            button.removeAttribute("data-copied");
            disown(button, "data-copied");
        }, COPIED_MS));
    }, function () {
        // refused (no permission, not a secure context): the button simply does not say it copied
    });
}

if (page) {
    listen("click", function (e) {
        const copier = near(e.target, "[data-rask-copy]");
        if (copier) {
            copy(copier);
        }
        const clearer = near(e.target, "[data-rask-clear]");
        const cleared = clearer ? named(clearer, "data-rask-clear") : null;
        if (isField(cleared) && cleared.value !== "") {
            cleared.value = "";
            fire(cleared, "input");
            fire(cleared, "change");
        }
        const focuser = near(e.target, "[data-rask-focus]");
        const target = cleared || (focuser ? named(focuser, "data-rask-focus") : null);
        if (target) {
            target.focus();
        }
    });

    // Capture phase on the document: ahead of the listener that sends the value to the page (rask-input,
    // on the bubble), so what is sent is what is shown.
    listen("input", function (e) {
        if (isField(e.target as Element) && !(e as InputEvent).isComposing) {
            const kind = (e as InputEvent).inputType || "";
            shape(e.target as Field, kind.indexOf("delete") === 0);
        }
    }, true);

    listen("keydown", function (e) {
        const t = e.target;
        if (!(t instanceof HTMLInputElement) || e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }
        // A switch is pressed with Enter as well as Space (Flux UI's is); a plain checkbox is left alone,
        // where Enter still submits the form around it.
        if (e.key === "Enter" && t.type === "checkbox" && t.getAttribute("role") === "switch" && !e.shiftKey) {
            e.preventDefault();
            t.click();
            return;
        }
        const big = t.type === "range" ? Number(t.getAttribute("data-rask-big-step")) : 0;
        if (!(big > 0)) {
            return;
        }
        const up = e.key === "PageUp" || (e.shiftKey && (e.key === "ArrowRight" || e.key === "ArrowUp"));
        const down = e.key === "PageDown" || (e.shiftKey && (e.key === "ArrowLeft" || e.key === "ArrowDown"));
        if (!up && !down) {
            return;
        }
        e.preventDefault();
        const before = t.value;
        t.valueAsNumber = t.valueAsNumber + (up ? big : -big); // the input clamps to min/max and snaps to step
        if (t.value !== before) {
            fire(t, "input");
            fire(t, "change");
        }
    }, true);
}
