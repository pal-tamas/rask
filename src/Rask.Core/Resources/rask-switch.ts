// A button that is a switch and keeps its own state (data-rask-switch).
//
// A toggle nobody listens to — no binding, no handler — only changes what it draws when it is pressed, and
// that is the browser's to do: a press flips aria-checked and data-checked on the button, and nothing is sent
// to the page. Enter and Space press a <button> by themselves, so the keys are the platform's.
// (Flux UI's toggle, measured: a press, Space and Enter each turn it; the arrow keys do nothing; a disabled
// one takes no press.)
//
// Both attributes are held against the morph (rask-owned.ts): the page rendered the state it starts in, and a
// later render must not put that back over what the reader chose.

import {listen, near, own, page} from "./rask-owned.js";

/** Turns the switch: what a press does. Exported for the fixture. */
export function turn(button: Element): boolean {
    const on = button.getAttribute("aria-checked") !== "true";
    own(button, "aria-checked");
    own(button, "data-checked");
    button.setAttribute("aria-checked", on ? "true" : "false");
    if (on) {
        button.setAttribute("data-checked", "");
    } else {
        button.removeAttribute("data-checked");
    }
    return on;
}

if (page) {
    listen("click", function (e) {
        const button = near(e.target, "button[data-rask-switch]");
        if (button && !(button as HTMLButtonElement).disabled) {
            turn(button);
        }
    });
}
