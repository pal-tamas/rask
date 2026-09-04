// Fullscreen — the Fullscreen API.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// `request` needs TRANSIENT USER ACTIVATION: it must run inside the call stack of a click or key
// press, not after an await. That is the whole reason Rask's C# side exposes this only on the WASM
// host and as a declarative gesture component on Server — a WebSocket round trip loses the
// activation. Calling it from TypeScript, in the handler, has no such problem.

export function isSupported(): boolean {
    return typeof document !== "undefined" && !!document.fullscreenEnabled;
}

export function isActive(): boolean {
    return document.fullscreenElement != null;
}

/** Go fullscreen. With no element, the whole page does. */
export function request(element?: Element | null): Promise<void> {
    return (element || document.documentElement).requestFullscreen();
}

/** Leave fullscreen. A no-op when nothing is fullscreen. */
export function exit(): Promise<void> {
    return document.fullscreenElement ? document.exitFullscreen() : Promise.resolve();
}
