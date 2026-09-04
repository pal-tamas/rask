// Media queries — window.matchMedia.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

/** Whether a CSS media query currently matches. */
export function matches(query: string): boolean {
    return window.matchMedia(query).matches;
}

/** The user's colour-scheme preference. */
export function prefersDark(): boolean {
    return matches("(prefers-color-scheme: dark)");
}

/**
 * Whether the user has asked the system to minimise animation. Honour it: it is an accessibility
 * setting, not a style preference.
 */
export function prefersReducedMotion(): boolean {
    return matches("(prefers-reduced-motion: reduce)");
}

/**
 * Watch a media query, calling back whenever it starts or stops matching. Returns the stop function.
 *
 * The listener is attached to the live MediaQueryList, which is exactly why this cannot be expressed
 * as a one-shot read — and why the C# side gets only `matches()` on the Server transport.
 */
export function watch(query: string, onChange: (matches: boolean) => void): () => void {
    const list = window.matchMedia(query);
    const handler = (e: MediaQueryListEvent) => onChange(e.matches);
    list.addEventListener("change", handler);

    let stopped = false;
    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        list.removeEventListener("change", handler);
    };
}
