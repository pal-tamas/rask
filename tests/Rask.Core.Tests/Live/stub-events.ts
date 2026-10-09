// The page a module that listens at import time finds: a document that keeps its listeners, frames that
// run when the fixture says so, and an Element to be an instance of.
//
// Imported for its effect, FIRST: rask-input.ts adds its listeners while it is being imported, so the
// globals have to be there before the import below it is evaluated.

type Heard = (e: { target: unknown }) => void;

/** The document's listeners, by event type. */
export const listeners: Record<string, Heard> = {};

const frames: (() => void)[] = [];

/** Runs every animation frame asked for so far. */
export function frame(): void {
    frames.splice(0).forEach(function (run) { run(); });
}

/** A field as rask-input reads one: its handlers as attributes, and what it says. */
export class StubField {
    value = "";
    isConnected = true;
    tagName = "INPUT";

    constructor(private readonly attributes: Record<string, string>) {
    }

    closest(): StubField {
        return this;
    }

    hasAttribute(name: string): boolean {
        return name in this.attributes;
    }

    getAttribute(name: string): string | null {
        return name in this.attributes ? this.attributes[name] : null;
    }
}

const page = globalThis as unknown as Record<string, unknown>;
page.window = globalThis;
page.Element = StubField;
page.document = {
    addEventListener(type: string, heard: Heard) { listeners[type] = heard; },
};
page.requestAnimationFrame = function (run: () => void) { return frames.push(run); };
page.cancelAnimationFrame = function () { frames.length = 0; };
