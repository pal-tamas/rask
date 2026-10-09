// Tabs: the arrow keys of a role="tablist".
//
// A tablist is a ring of buttons of which ONE is a tab stop (the roving tabindex), so the arrow keys are how the
// others are reached — and C# can neither move focus nor keep an arrow from scrolling the page. ArrowRight and
// ArrowDown go to the next tab, ArrowLeft and ArrowUp to the previous, past any that is disabled and around the
// ends. The tab arrived at is focused AND pressed, so its own click handler selects it exactly as a pointer would:
// selection follows focus. From a button in the row that is not a tab ("Add tab") the arrows start at the selected
// tab. Only `button[role=tab]` takes part — a tab that is a link goes somewhere, and an arrow must not navigate.
// Home and End are not handled: they stay the page's. Measured on Flux UI's tabs, key for key.
//
// The role is the whole request: there is no data-rask-* attribute to write. A page with a `[role=tablist]`
// loads the hooks (rask-hook-loader.ts), as one with an `input[role=switch]` does.

import {listen, near, page} from "./rask-owned.js";

/** The index an arrow key lands on: the next tab that way which is not disabled, around the ends; -1 with none. */
export function tabAfter(disabled: readonly boolean[], from: number, step: 1 | -1): number {
    const count = disabled.length;
    const start = from >= 0 ? from : step > 0 ? -1 : count;
    for (let i = 1; i <= count; i++) {
        const at = (((start + step * i) % count) + count) % count;
        if (!disabled[at]) {
            return at;
        }
    }
    return -1;
}

if (page) {
    listen("keydown", function (e) {
        if (e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }
        const step = e.key === "ArrowRight" || e.key === "ArrowDown" ? 1
            : e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 0;
        const target = e.target;
        const list = step !== 0 && target instanceof HTMLButtonElement ? near(target, "[role=tablist]") : null;
        if (!list) {
            return;
        }

        const tabs: HTMLButtonElement[] = [];
        for (const tab of list.querySelectorAll("button[role=tab]")) {
            if (tab instanceof HTMLButtonElement && tab.closest("[role=tablist]") === list) {
                tabs.push(tab);
            }
        }
        const here = tabs.findIndex(function (tab) { return tab === target; });
        const from = here >= 0 ? here : tabs.findIndex(function (tab) { return tab.getAttribute("aria-selected") === "true"; });
        const to = tabAfter(tabs.map(function (tab) { return tab.disabled; }), from, step === 1 ? 1 : -1);
        if (to < 0) {
            return;
        }

        e.preventDefault();
        if (to !== here) {
            tabs[to].focus();
            tabs[to].click();
        }
    });
}
