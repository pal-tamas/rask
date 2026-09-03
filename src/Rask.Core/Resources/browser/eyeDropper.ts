// Screen colour picker — EyeDropper.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
// Chromium-only, secure context, and needs transient user activation — see ./fullscreen.ts.

/** lib.dom does not declare EyeDropper: it is Chromium-family only. */
interface EyeDropperLike {
    open(): Promise<{ sRGBHex: string }>;
}

type EyeDropperCtor = { new(): EyeDropperLike } | undefined;

function constructor(): EyeDropperCtor {
    return typeof globalThis === "undefined"
        ? undefined
        : (globalThis as unknown as { EyeDropper?: EyeDropperCtor }).EyeDropper;
}

export function isSupported(): boolean {
    return !!constructor();
}

/**
 * Open the picker and resolve with the chosen colour as `#rrggbb`.
 *
 * Resolves NULL when the user cancels with Escape. The platform rejects with an AbortError there,
 * which reads as a failure at the call site when it is an ordinary outcome — the caller wants an
 * if, not a try.
 */
export function open(): Promise<string | null> | null {
    // Bound to a local so the narrowing survives: a support helper cannot narrow for the checker.
    const ctor = constructor();
    if (!ctor) {
        return null;
    }
    return new ctor().open().then((r) => r.sRGBHex, () => null);
}
