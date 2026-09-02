// Screen colour picker — EyeDropper.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
// Chromium-only, secure context, and needs transient user activation — see ./fullscreen.ts.

export function isSupported(): boolean {
    return typeof EyeDropper !== "undefined" && !!EyeDropper;
}

/**
 * Open the picker and resolve with the chosen colour as `#rrggbb`.
 *
 * Resolves NULL when the user cancels with Escape. The platform rejects with an AbortError there,
 * which reads as a failure at the call site when it is an ordinary outcome — the caller wants an
 * if, not a try.
 */
export function open(): Promise<string | null> | null {
    // Bound to a local so the narrowing survives: EyeDropper is declared as possibly undefined, and a
    // support helper cannot narrow a global for the type checker.
    const ctor = typeof EyeDropper === "undefined" ? undefined : EyeDropper;
    if (!ctor) {
        return null;
    }
    return new ctor().open().then((r) => r.sRGBHex, () => null);
}
