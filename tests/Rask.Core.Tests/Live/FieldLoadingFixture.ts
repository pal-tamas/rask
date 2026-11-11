// Script fixture for the field half of rask-loading.ts — the mark an input carries while the event that sent its
// value is unanswered (Livewire's data-loading on a `wire:model.live` input).
//
// Timers are faked, and `document.querySelector` finds the field by the handler attribute the payload names, as the
// browser would. The C# test (FieldLoadingTests) runs this on Jint and asserts what it reports.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";
import {
    beginLoading,
    endLoading,
    fieldTarget,
    LOADING_DELAY_MS,
} from "../../../src/Rask.Core/Resources/rask-loading.js";

let now = 0;
let nextId = 1;
const timers = new Map<number, {at: number; fn: () => void}>();
const g = globalThis as unknown as {
    setTimeout: (fn: () => void, ms: number) => number;
    clearTimeout: (id: number) => void;
    document: unknown;
};
g.setTimeout = (fn, ms) => {
    const id = nextId++;
    timers.set(id, {at: now + ms, fn});
    return id;
};
g.clearTimeout = (id) => { timers.delete(id); };

function advance(ms: number): void {
    const until = now + ms;
    for (;;) {
        let due: [number, {at: number; fn: () => void}] | null = null;
        for (const entry of timers) {
            if (entry[1].at <= until && (!due || entry[1].at < due[1].at)) due = entry;
        }
        if (!due) break;
        timers.delete(due[0]);
        now = due[1].at;
        due[1].fn();
    }
    now = until;
}

// An element as the module reads one: its attributes and the element above it.
class Field {
    readonly attributes = new Map<string, string>();
    nodeType = 1;

    constructor(readonly tagName: string, attributes: Record<string, string>, readonly parentNode: Field | null = null) {
        for (const [name, value] of Object.entries(attributes)) this.attributes.set(name, value);
    }

    getAttribute(name: string): string | null { return this.attributes.get(name) ?? null; }
    hasAttribute(name: string): boolean { return this.attributes.has(name); }
    setAttribute(name: string, value: string): void { this.attributes.set(name, value); }
    removeAttribute(name: string): void { this.attributes.delete(name); }
}

const live = new Field("INPUT", {"data-rask-on-input": "h1"});
const changed = new Field("SELECT", {"data-rask-on-change": "h2"});
const quietParent = new Field("DIV", {"data-rask-loading": "off"});
const quiet = new Field("INPUT", {"data-rask-on-input": "h3"}, quietParent);
// A search field that asks a store once typing pauses: its `input` goes out under the pause's handler.
const paused = new Field("INPUT", {"data-rask-on-pause": "h4", "data-rask-debounce": "250"});
const all = [live, changed, quiet, paused];
g.document = {
    querySelector(selector: string) {
        const found = /^\[(data-rask-on-\w+)="(\w+)"\]$/.exec(selector);
        return found ? all.find((field) => field.getAttribute(found[1]) === found[2]) ?? null : null;
    },
};

const target = {
    input: fieldTarget({id: "h1", type: "input"}) === live,
    change: fieldTarget({id: "h2", type: "change"}) === changed,
    click: fieldTarget({id: "h1", type: "click"}) === null,
    unasked: fieldTarget({id: "h1", type: "input", unasked: true}) === null,
    gone: fieldTarget({id: "h9", type: "input"}) === null,
    optedOut: fieldTarget({id: "h3", type: "input"}) === null,
    paused: fieldTarget({id: "h4", type: "input"}) === paused,
    pausedChange: fieldTarget({id: "h4", type: "change"}) === null,
};

const ticket = beginLoading(live as unknown as Element, null, false);
const beforeDelay = live.hasAttribute("data-loading");
advance(LOADING_DELAY_MS);
const duringLoading = live.hasAttribute("data-loading");
const busyWhileLoading = live.hasAttribute("aria-busy");
endLoading(ticket);
const afterEnd = live.hasAttribute("data-loading");

// A button that waits still says aria-busy: the field's way is the exception.
const button = new Field("BUTTON", {});
const pressed = beginLoading(button as unknown as Element);
advance(LOADING_DELAY_MS);
const buttonBusy = button.hasAttribute("aria-busy");
endLoading(pressed);

// A list's search field says it waits the moment its question leaves (`data-rask-loading-now`); a button beside it
// still waits the delay out.
const search = new Field("INPUT", {"data-rask-on-pause": "h5", "data-rask-loading-now": ""});
const asked = beginLoading(search as unknown as Element, null, false);
const nowAtOnce = search.hasAttribute("data-loading");
const nowBusy = search.hasAttribute("aria-busy");
const other = new Field("BUTTON", {});
const otherTicket = beginLoading(other as unknown as Element);
const otherAtOnce = other.hasAttribute("data-loading");
advance(LOADING_DELAY_MS);
const nowStill = search.hasAttribute("data-loading");
endLoading(asked);
endLoading(otherTicket);
const nowAfterEnd = search.hasAttribute("data-loading");

// Answered within the same frame, it is marked and unmarked: nothing of the mark is left behind.
const quick = new Field("INPUT", {"data-rask-loading-now": ""});
endLoading(beginLoading(quick as unknown as Element, null, false));
const quickLeft = quick.hasAttribute("data-loading");

report({
    now: {atOnce: nowAtOnce, busy: nowBusy, otherAtOnce, still: nowStill, afterEnd: nowAfterEnd, quickLeft},
    target,
    beforeDelay,
    duringLoading,
    busyWhileLoading,
    afterEnd,
    buttonBusy,
});
