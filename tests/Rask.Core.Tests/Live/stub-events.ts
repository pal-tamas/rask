// The page a module that listens at import time finds: a document that keeps its listeners, frames and
// timers that run when the fixture says so, and an Element to be an instance of.
//
// Imported for its effect, FIRST: rask-input.ts adds its listeners while it is being imported, so the
// globals have to be there before the import below it is evaluated.

type Heard = (e: { target: unknown; isComposing?: boolean }) => void;

/** The document's listeners, by event type. */
export const listeners: Record<string, Heard> = {};

const frames: (() => void)[] = [];

/** Runs every animation frame asked for so far. */
export function frame(): void {
    frames.splice(0).forEach(function (run) { run(); });
}

const timers = new Map<number, { at: number; run: () => void }>();
let now = 0;
let nextTimer = 1;

/** Lets this many milliseconds pass, running the timers that fall due. */
export function elapse(ms: number): void {
    now += ms;
    Array.from(timers).forEach(function (entry) {
        if (entry[1].at <= now && timers.delete(entry[0])) entry[1].run();
    });
}

/** A field as rask-input reads one: its handlers as attributes, and what it says. */
export class StubField {
    value = "";
    isConnected = true;
    tagName = "INPUT";

    constructor(private readonly attributes: Record<string, string>) {
    }

    // `[a], [b]`: the field itself when it carries one of the attributes named, as the nearest such element.
    closest(selector: string): StubField | null {
        const named = selector.match(/[\w-]+(?=])/g) || [];
        return named.some((name) => name in this.attributes) ? this : null;
    }

    hasAttribute(name: string): boolean {
        return name in this.attributes;
    }

    getAttribute(name: string): string | null {
        return name in this.attributes ? this.attributes[name] : null;
    }

    removeAttribute(name: string): void {
        delete this.attributes[name];
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
page.setTimeout = function (run: () => void, ms: number) {
    timers.set(nextTimer, {at: now + ms, run});
    return nextTimer++;
};
page.clearTimeout = function (timer: number) { timers.delete(timer); };
