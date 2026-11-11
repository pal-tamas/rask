// Script fixture for the press on a list's create row (rask-clicks.ts): taken while the row is on offer, refused while
// the list's search is out and the row says `aria-disabled="true"` (rask-list.ts), and refused again while the press
// before it is still being made (rask-loading.ts, `data-rask-loading-now`).
//
// A row is a stub that answers the selectors the guard asks. The C# test (CreateRowClickTests) runs this on Jint.

import {report} from "../../Rask.TestSupport/ScriptFixtures/fixture.js";

class StubElement {
}

const page = globalThis as unknown as Record<string, unknown>;
page.window = globalThis;
page.Element = StubElement;
page.setTimeout = () => 0;
page.clearTimeout = () => undefined;

const {setHost} = await import("../../../src/Rask.Core/Resources/rask-host.js");
setHost({send: () => undefined, inRoot: () => true});
const {handlerClick} = await import("../../../src/Rask.Core/Resources/rask-clicks.js");
const {beginLoading, endLoading, loadingTarget} = await import("../../../src/Rask.Core/Resources/rask-loading.js");

class Row extends StubElement {
    readonly tagName = "DIV";
    readonly nodeType = 1;
    readonly parentNode = null;
    readonly isConnected = true;

    constructor(readonly attributes: Record<string, string>) {
        super();
    }

    getAttribute(name: string): string | null { return name in this.attributes ? this.attributes[name] : null; }

    hasAttribute(name: string): boolean { return name in this.attributes; }

    setAttribute(name: string, value: string): void { this.attributes[name] = value; }

    removeAttribute(name: string): void { delete this.attributes[name]; }

    // The row carries the handler, and is nothing else the guard asks about: no button, no invoker.
    closest(selector: string): Row | null { return selector === "[data-rask-on-click]" ? this : null; }
}

type Press = { target: unknown; button: number; defaultPrevented: boolean; preventDefault(): void };

function press(row: Row): { sent: boolean; prevented: boolean } {
    const e: Press = {target: row, button: 0, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }};
    const sent = handlerClick(e as unknown as MouseEvent) === row;
    return {sent, prevented: e.defaultPrevented};
}

const marks = {
    "data-rask-on-click": "h1", "data-rask-list-create": "1", "data-rask-loading": "", "data-rask-loading-now": "",
};
const offered = press(new Row({...marks}));
const searching = press(new Row({...marks, "aria-disabled": "true"}));
// Not a create row: a row saying aria-disabled is the page's own to refuse, as before.
const other = press(new Row({"data-rask-on-click": "h2", "aria-disabled": "true"}));

// Pressed: it waits at once, and a second press while it does is dropped; once its handler is done it is pressable.
const made = new Row({...marks});
const first = press(made);
const ticket = beginLoading(loadingTarget(made as unknown as Element), made as unknown as Element);
const spins = made.hasAttribute("data-loading");
const busy = made.getAttribute("aria-busy");
const second = press(made);
endLoading(ticket);
const third = press(made);

report({
    offered, searching, other,
    pressed: {first: first.sent, spins, busy, second: second.sent, third: third.sent},
});
