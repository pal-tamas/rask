// The globals Rask's own runtime puts on the page, described for TypeScript.
//
// Shipped with the build integration and compiled alongside every project's scoped assets, so a
// component's `.ts` can call into .NET without each app hand-declaring the same shapes. Nothing here
// is emitted: a `.d.ts` produces no JavaScript, and it is never registered as a scoped asset.
//
// Deliberately narrow. These describe what Rask itself installs; anything an app's own third-party
// library adds belongs in a `.d.ts` beside that app's code.

/** The .NET-side dispatcher the WASM host installs on `window`. */
interface RaskDotNetInterop {
    /**
     * Calls a `[JSInvokable]` static method and resolves with its return value.
     *
     * @param assemblyName The assembly the method lives in.
     * @param methodIdentifier The `[JSInvokable]` name, which need not be the method's own name.
     */
    invokeMethodAsync<T = unknown>(
        assemblyName: string,
        methodIdentifier: string,
        ...args: unknown[]): Promise<T>;
}

interface Window {
    /**
     * Present on the WASM host. The Server host reaches C# over its WebSocket instead, so code that
     * touches this is reachable only from a WASM-hosted component.
     */
    DotNet: RaskDotNetInterop;

    /**
     * The namespace every component's scoped exports are hung on, keyed by the component's SIMPLE
     * type name — `window.Rask["Counter"].mount(...)`.
     *
     * Typed loosely because its shape is the union of every component in the app, which is not
     * knowable from here. A component calling into its OWN exports should import them instead; this
     * exists for the cross-component case, and for code that has to check whether a namespace has
     * been registered yet.
     */
    Rask: Record<string, Record<string, (...args: never[]) => unknown> | undefined>;
}
