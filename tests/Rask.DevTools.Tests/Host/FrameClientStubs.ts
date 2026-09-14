// The stub DOM the WASM panel's frame client loads into (FrameClientFixture). Imported FIRST, for its side effect: the
// frame client and the Rask.Core modules it bundles bind their listeners the moment they load, so the globals have to
// exist before that happens. Records every listener, and every message the frame posts to its page.

type Handler = (e: unknown) => void;

export const documentListeners: {type: string; handler: Handler}[] = [];
export const windowListeners: {type: string; handler: Handler}[] = [];
export const posted: unknown[] = [];

export class StubElement {
    readonly attributes = new Map<string, string>();
    type = "";
    checked = false;
    value = "";
    constructor(public tagName: string) {}
    getAttribute(name: string) { return this.attributes.get(name) ?? null; }
    hasAttribute(name: string) { return this.attributes.has(name); }
    setAttribute(name: string, value: string) { this.attributes.set(name, value); }
    closest(selector: string) {
        // Enough for the listeners under test: `[a]`, or `[a], [b]`.
        const names = selector.split(",").map(s => s.trim().replace(/^\[|\]$/g, ""));
        return names.some(n => this.attributes.has(n)) ? this : null;
    }
    addEventListener() {}
    querySelectorAll() { return []; }
}

const body = new StubElement("BODY");
const noop = () => {};
const globals = globalThis as unknown as Record<string, unknown>;
globals.Element = StubElement;
globals.HTMLElement = StubElement;
globals.MutationObserver = class { observe() {} disconnect() {} };
globals.document = {
    addEventListener: (type: string, handler: Handler) => documentListeners.push({type, handler}),
    removeEventListener: noop,
    body: Object.assign(body, {contains: () => true}),
    head: new StubElement("HEAD"),
    documentElement: new StubElement("HTML"),
    querySelector: () => null,
    querySelectorAll: () => [],
    getElementById: () => null,
};
const parent = {postMessage: (message: unknown) => posted.push(message)};
globals.window = {
    addEventListener: (type: string, handler: Handler) => windowListeners.push({type, handler}),
    removeEventListener: noop,
    parent,
};
