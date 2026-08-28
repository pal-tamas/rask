// The contract between Rask's shared browser modules and whichever host is running them.
//
// The Server client and the WASM client do the same things by completely different means — one
// writes to a WebSocket, the other calls a [JSExport] through JSImport — so the shared modules
// cannot import either. Before this, they did not have to: everything was spliced into one enormous
// function scope, and a module simply *called* `send` because the host happened to have declared it
// somewhere above. Nothing recorded that dependency, and nothing checked it.
//
// Measured, the whole cross-cutting surface is two functions. That is what this file states.
//
// A mutable binding set once at boot, rather than a parameter threaded through every call: ES module
// bindings are live, so an importer sees whatever the host installed, and the alternative would mean
// passing a context object through the event router, the input coalescer and every handler they
// reach — for two functions that never change after boot.

/** What a host must provide before any shared module runs. */
export interface RaskHost {
    /**
     * Ships an event payload to .NET. Over a WebSocket on the Server host; through JSExport on WASM.
     */
    send(payload: unknown): void;

    /**
     * Whether an element is inside the live render root.
     *
     * Events are bound at the document, so this is what keeps a click in some third-party widget
     * outside the app from being dispatched as if it were a component's.
     */
    inRoot(el: Node | null): boolean;
}

/**
 * Fails loudly rather than silently doing nothing.
 *
 * A shared module reaching the host before boot is a wiring bug, and the alternative — a no-op
 * default — is the shape where events are simply dropped and the app looks merely unresponsive.
 */
let host: RaskHost = {
    send() {
        throw new Error("Rask: send() was called before a host was installed.");
    },
    inRoot() {
        throw new Error("Rask: inRoot() was called before a host was installed.");
    },
};

/** Installs the running host. Called once, by the host entry point, before anything dispatches. */
export function setHost(h: RaskHost): void {
    host = h;
}

/** Ships an event payload to .NET. */
export function send(payload: unknown): void {
    host.send(payload);
}

/** Whether an element is inside the live render root. */
export function inRoot(el: Node | null): boolean {
    return host.inRoot(el);
}
