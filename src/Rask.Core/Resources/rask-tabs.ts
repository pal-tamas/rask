// Tabs: a role="tablist" whose selection is the browser's.
//
// Which tab is selected, and which panel shows, is only what is DRAWN — so a press on a tab selects it here, in
// the press, and the page is sent nothing for it (Flux UI's tabs, measured: its script writes `aria-selected`,
// `tabindex` and `data-selected` on the tabs, `hidden`, `tabindex` and `data-selected` on the panels each tab's
// `aria-controls` names, and Livewire hears only a model). A tablist asks for it with `data-rask-tabs`, each
// tab says what it is called in `data-rask-tab`, and everything that paints a selected tab reads
// `[data-selected]`. All of it is held against the morph (rask-owned.ts).
//
// THE VALUE. A tablist the page reads holds ONE hidden field as its first child, bound the usual way: the
// selected tab's name is written into it and announced with `input` and `change`, so a `.Bind(…)` carries it
// with the reader's next action, `.Live()` and `OnChange` at once — the field's own timing, nothing of this
// module's.
//
// THE PAGE'S WORD. `data-rask-tabs="<turn> <name>"` is the tab the page says is selected. When it changes and
// the turn has not, the page is answering what this told it: an echo, possibly late, and the reader may be
// two tabs further on — it is not shown. A new turn is the page choosing a tab itself (a save that was
// refused opens the tab with the first mistake), and that one is shown whatever the reader last picked.
//
// THE ARROW KEYS. A tablist is a ring of buttons of which ONE is a tab stop (the roving tabindex), so the arrow
// keys are how the others are reached. ArrowRight and ArrowDown go to the next tab, ArrowLeft and ArrowUp to
// the previous, past any that is disabled and around the ends. The tab arrived at is focused AND pressed:
// selection follows focus. From a button in the row that is not a tab ("Add tab") the arrows start at the
// selected tab. Only `button[role=tab]` takes part. Home and End are not handled: they stay the page's.
// Measured on Flux UI's tabs, key for key.
//
// A page with a `[role=tablist]` loads the hooks (rask-hook-loader.ts), as one with an `input[role=switch]` does.

import {listen, named, near, own, page} from "./rask-owned.js";

const LIST = "[role=tablist]";
const SAYS = "data-rask-tabs";

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

function wordOf(said: string | null): [string, string | null] {
    const cut = said ? said.indexOf(" ") : -1;
    return cut < 0 ? ["", null] : [said!.slice(0, cut), said!.slice(cut + 1)];
}

/**
 * What a tablist does with the page's word on which tab is selected: `null` when it is the answer to something
 * the reader chose (taken out of `told`, with every answer owed before it), else the name to show.
 * Exported for the fixture.
 */
export function pageSays(before: string | null, after: string | null, told: string[]): string | null {
    const [turn, name] = wordOf(after);
    if (name === null) {
        return null;
    }
    const echo = turn === wordOf(before)[0] ? told.indexOf(name) : -1;
    if (echo >= 0) {
        told.splice(0, echo + 1);
        return null;
    }
    told.length = 0;
    return name;
}

const told = new WeakMap<Element, string[]>();

function tabsOf(list: Element): HTMLButtonElement[] {
    const tabs: HTMLButtonElement[] = [];
    for (const tab of list.querySelectorAll("button[role=tab]")) {
        if (tab instanceof HTMLButtonElement && tab.closest(LIST) === list) {
            tabs.push(tab);
        }
    }
    return tabs;
}

function fieldOf(list: Element): HTMLInputElement | null {
    const first = list.firstElementChild;
    return first instanceof HTMLInputElement && first.type === "hidden" ? first : null;
}

function mark(el: Element, name: string, value: string | null): void {
    own(el, name);
    if (value === null) {
        el.removeAttribute(name);
    } else if (el.getAttribute(name) !== value) {
        el.setAttribute(name, value);
    }
}

// Draws `chosen` selected and its panel shown, every other tab of the row not.
function draw(list: Element, chosen: Element): void {
    const shows = named(chosen, "aria-controls");
    // A panel find-in-page may reach is hidden `until-found`, and says so while it is.
    const hides = (shows && shows.getAttribute("hidden")) || "";
    for (const tab of tabsOf(list)) {
        const on = tab === chosen;
        mark(tab, "aria-selected", on ? "true" : "false");
        mark(tab, "tabindex", on ? "0" : "-1");
        mark(tab, "data-selected", on ? "" : null);
        const panel = named(tab, "aria-controls");
        if (panel) {
            mark(panel, "tabindex", on ? "0" : "-1");
            mark(panel, "data-selected", on ? "" : null);
            mark(panel, "hidden", on ? null : panel.getAttribute("hidden") ?? hides);
        }
    }
}

// The reader chose `tab`: drawn at once, and written where the page reads it.
function choose(list: Element, tab: Element): void {
    draw(list, tab);
    const field = fieldOf(list);
    const name = tab.getAttribute("data-rask-tab") || "";
    if (!field || field.value === name) {
        return;
    }
    let waiting = told.get(list);
    if (!waiting) {
        told.set(list, waiting = []);
    }
    waiting.push(name);
    if (waiting.length > 16) {
        waiting.shift(); // a page that never answers must not grow this for ever
    }
    field.value = name;
    field.dispatchEvent(new Event("input", {bubbles: true}));
    field.dispatchEvent(new Event("change", {bubbles: true}));
}

// The page wrote which tab is selected.
function follow(list: Element, before: string | null): void {
    const name = pageSays(before, list.getAttribute(SAYS), told.get(list) || []);
    if (name === null) {
        return;
    }
    const field = fieldOf(list);
    if (field && field.value !== name) {
        field.value = name;
    }
    for (const tab of tabsOf(list)) {
        if (tab.getAttribute("data-rask-tab") === name) {
            draw(list, tab);
            return;
        }
    }
}

if (page) {
    listen("keydown", function (e) {
        if (e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }
        const step = e.key === "ArrowRight" || e.key === "ArrowDown" ? 1
            : e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 0;
        const target = e.target;
        const list = step !== 0 && target instanceof HTMLButtonElement ? near(target, LIST) : null;
        if (!list) {
            return;
        }

        const tabs = tabsOf(list);
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

    listen("click", function (e) {
        const tab = near(e.target, "button[role=tab]");
        const list = tab && tab.closest(LIST);
        if (tab && list && list.hasAttribute(SAYS)) {
            choose(list, tab);
        }
    });

    // Find-in-page is about to reveal a panel that is not shown: its tab is the one selected.
    listen("beforematch", function (e) {
        const panel = near(e.target, "[role=tabpanel]");
        const tab = panel && named(panel, "aria-labelledby");
        const list = tab && tab.closest(LIST);
        if (tab && list && list.hasAttribute(SAYS) && tab.getAttribute("aria-selected") !== "true") {
            choose(list, tab);
        }
    }, true);

    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            for (const record of records) {
                follow(record.target as Element, record.oldValue);
            }
        }).observe(page.documentElement, {subtree: true, attributes: true, attributeOldValue: true, attributeFilter: [SAYS]});
    }
}
