// Node-driven fixture for rask-bound.ts — the one hidden bound field of a group the browser owns — and what a
// render that lands on it as a morph does: the WebAssembly host morphs every render, and the morph writes the
// rendered `value` back onto every field, changed or not.
//
// A hidden input's `value` IS its attribute, so the stub field below keeps one string for both, as the browser
// does. The observer is a stub the fixture hands records to, the way the browser would after a render. The C#
// test (BoundFieldClientTests) runs this in a node subprocess and asserts the JSON line.

interface Observed {
    type: string;
    target: unknown;
    addedNodes: unknown[];
}

let observer: ((records: Observed[]) => void) | null = null;
let queued: Observed[] = [];

class Field {
    readonly type = "hidden";
    private said: string;

    constructor(value: string) {
        this.said = value;
    }

    get value(): string { return this.said; }

    set value(value: string) {
        this.said = value;
        queued.push({type: "attributes", target: this, addedNodes: []});
    }

    getAttribute(name: string): string | null { return name === "value" ? this.said : null; }

    setAttribute(name: string, value: string): void {
        if (name === "value") this.value = value;
    }

    closest(): unknown { return group; }

    dispatchEvent(): boolean { return true; }
}

const field = new Field("[]");
const group = {
    matches(selector: string): boolean { return selector === "[data-group]"; },
    querySelector(): Field { return field; },
    querySelectorAll(): unknown[] { return []; },
};

const scope = globalThis as unknown as Record<string, unknown>;
scope.HTMLInputElement = Field;
scope.MutationObserver = class {
    constructor(heard: (records: Observed[]) => void) { observer = heard; }
    observe(): void {}
    takeRecords(): Observed[] {
        const taken = queued;
        queued = [];
        return taken;
    }
};
scope.document = {
    addEventListener(): void {},
    documentElement: {},
    querySelectorAll(selector: string): unknown[] { return selector === "[data-group]" ? [group] : []; },
};

const {announce, bind} = await import("../../../src/Rask.Core/Resources/rask-bound.js");

// What the group shows, and what the page made it show.
let shows = "[]";
const adopted: string[] = [];
bind("[data-group]", () => shows, function (_group: Element, rendered: string) {
    adopted.push(rendered);
    shows = rendered;
});

/** A render puts `value` on the field, as a morph does, and the browser tells the observer. */
function render(value: string): void {
    field.setAttribute("value", value);
    const records = queued;
    queued = [];
    observer!(records);
}

/** The reader makes the group say `value`, and the hook announces it. */
function pick(value: string): void {
    shows = value;
    announce(group as unknown as Element, value);
}

pick("[\"pro\"]");
render("[]");
const keptAcrossARenderThatSaidNothingNew = shows === "[\"pro\"]" && field.value === "[\"pro\"]" && adopted.length === 0;

render("[\"pro\"]");
const echoIsNoNews = shows === "[\"pro\"]" && adopted.length === 0;

render("[\"team\"]");
const aNewAnswerIsAdopted = shows === "[\"team\"]" && field.value === "[\"team\"]" && adopted.join() === "[\"team\"]";

pick("[\"free\"]");
render("[\"team\"]");
const theNewAnswerRenderedAgainIsNoNewsEither = shows === "[\"free\"]" && field.value === "[\"free\"]" && adopted.length === 1;

process.stdout.write(JSON.stringify({
    keptAcrossARenderThatSaidNothingNew,
    echoIsNoNews,
    aNewAnswerIsAdopted,
    theNewAnswerRenderedAgainIsNoNewsEither,
}) + "\n");
