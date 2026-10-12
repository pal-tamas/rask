// What OpenStateFixture needs on top of stub-events.ts: a popover that opens and shuts the way the browser's
// does — `beforetoggle` at once, one `toggle` a task later for everything since the last — and a
// MutationObserver the fixture delivers to by hand.
//
// Imported for its effect, after stub-events.ts and BEFORE rask-dom.ts: that module starts its observers
// while it is being imported.

import {listeners, StubField} from "./stub-events.js";

type Heard = (records: { type: string; target: unknown }[]) => void;

const observers: { heard: Heard; watches: string[] }[] = [];

const page = globalThis as unknown as Record<string, unknown>;
const doc = page.document as Record<string, unknown>;
doc.documentElement = new StubField({});
doc.querySelector = function () { return null; };
(doc.documentElement as unknown as Record<string, unknown>).querySelectorAll = function () { return []; };
page.addEventListener = function () {};
page.MutationObserver = class {
    constructor(private readonly heard: Heard) {
    }

    observe(_root: unknown, options: { attributeFilter?: string[] }): void {
        observers.push({heard: this.heard, watches: options.attributeFilter ?? []});
    }
};

/** A popover, a dialog or a details as the runtime reads one. */
export class StubPopover extends StubField {
    private shown = false;
    private from: string | null = null;

    constructor(private readonly marks: Record<string, string>, tagName = "DIV") {
        super(marks);
        this.tagName = tagName;
    }

    get open(): boolean {
        return this.shown;
    }

    matches(selector: string): boolean {
        return selector === ":popover-open" && this.shown;
    }

    setAttribute(name: string, value: string): void {
        this.marks[name] = value;
    }

    showPopover(): void {
        this.set(true);
    }

    hidePopover(): void {
        this.set(false);
    }

    private set(open: boolean): void {
        if (this.shown === open) {
            return;
        }
        const states = {oldState: open ? "closed" : "open", newState: open ? "open" : "closed"};
        listeners.beforetoggle({type: "beforetoggle", target: this, ...states});
        this.from ??= states.oldState;
        this.shown = open;
    }

    /** The task the browser raises `toggle` in: one event for everything since the last. */
    raise(): void {
        if (this.from === null) {
            return;
        }
        const oldState = this.from;
        this.from = null;
        listeners.toggle({type: "toggle", target: this, oldState, newState: this.shown ? "open" : "closed"});
    }

    /** A render wrote `value` to attribute `name`, and the observers that watch it hear of it. */
    rendered(name: string, value: string): void {
        this.marks[name] = value;
        for (const observer of observers) {
            if (observer.watches.indexOf(name) >= 0) observer.heard([{type: "attributes", target: this}]);
        }
    }
}
