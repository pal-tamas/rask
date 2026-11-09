// Script fixture for rask-overlay.ts — when a press outside a Ui.Modal (data-rask-modal) dismisses it.
//
// The hooks listen on the document as they are imported, so the page they find is made first: a document that
// keeps its listeners, and one open modal dialog whose box is 100..300 on both axes. Each press is raised as the
// browser raises one — `pointerdown`, then `click` with `detail` counting the presses of the sequence. The C#
// test (PressOutsideClientTests) runs this on Jint and asserts what it reports.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";

type Heard = (e: unknown) => void;

const heard: Record<string, Heard[]> = {};

class StubElement {
    readonly attributes: Record<string, string>;

    constructor(attributes: Record<string, string> = {}) {
        this.attributes = attributes;
    }

    hasAttribute(name: string): boolean { return name in this.attributes; }

    getAttribute(name: string): string | null { return name in this.attributes ? this.attributes[name] : null; }

    setAttribute(name: string, value: string): void { this.attributes[name] = value; }

    removeAttribute(name: string): void { delete this.attributes[name]; }

    closest(): null { return null; }

    contains(other: unknown): boolean { return other === this; }

    matches(): boolean { return false; }

    querySelector(): null { return null; }
}

class StubHTMLElement extends StubElement {
}

// A dialog that is showing: how many `cancel`s it was sent, and how many times it closed.
class StubDialog extends StubHTMLElement {
    open = true;
    cancels = 0;

    getBoundingClientRect(): {left: number; right: number; top: number; bottom: number} {
        return {left: 100, right: 300, top: 100, bottom: 300};
    }

    dispatchEvent(e: {type: string; defaultPrevented: boolean}): boolean {
        if (e.type === "cancel") this.cancels++;
        return !e.defaultPrevented;
    }

    close(): void { this.open = false; }
}

// An engine that runs invoker commands itself: the hooks' fallback for one that does not stays out of the way.
class StubButton extends StubHTMLElement {
}

(StubButton.prototype as unknown as Record<string, unknown>).commandForElement = null;

const page = globalThis as unknown as Record<string, unknown>;
page.window = globalThis;
page.Element = StubElement;
page.HTMLElement = StubHTMLElement;
page.HTMLDialogElement = StubDialog;
page.HTMLButtonElement = StubButton;
page.HTMLInputElement = class extends StubHTMLElement {};
page.Node = StubElement;
page.Event = class {
    defaultPrevented = false;
    readonly cancelable: boolean;

    constructor(readonly type: string, init?: {cancelable?: boolean}) {
        this.cancelable = !!(init && init.cancelable);
    }

    preventDefault(): void { if (this.cancelable) this.defaultPrevented = true; }
};
page.document = {
    documentElement: new StubElement(),
    body: null,
    activeElement: null,
    addEventListener(type: string, listener: Heard) { (heard[type] ||= []).push(listener); },
    querySelectorAll() { return []; },
    getElementById() { return null; },
};

await import("../../../src/Rask.Core/Resources/rask-overlay.js");

// One press of a mouse at (x, y), the `detail`-th of its sequence, as every listener on the document hears it.
function press(target: StubElement, x: number, y: number, detail: number): void {
    const event = {
        target, clientX: x, clientY: y, detail, pointerType: "mouse", pointerId: 1, defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
        stopImmediatePropagation() { /* nothing after it here */ },
    };
    for (const type of ["pointerdown", "pointerup", "click"]) {
        for (const listener of heard[type] || []) listener(event);
    }
}

function modal(): StubDialog {
    return new StubDialog({"data-rask-modal": "any"});
}

// A single press on the backdrop dismisses it: a `cancel`, then the close.
const once = modal();
press(once, 10, 10, 1);

// The second press of a double one lands on the backdrop of a dialog the first press opened — the leave guard's
// question, raised by an editor's cross — and is not taken as the reader's answer to it.
const asked = modal();
press(asked, 10, 10, 2);

// A press inside the box is no press outside, the first of its sequence or not.
const inside = modal();
press(inside, 150, 150, 1);

report({
    singlePressCancels: once.cancels,
    singlePressCloses: !once.open,
    secondPressCancels: asked.cancels,
    secondPressLeavesItOpen: asked.open,
    insideCancels: inside.cancels,
    insideLeavesItOpen: inside.open,
});
