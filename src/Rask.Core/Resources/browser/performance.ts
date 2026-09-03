// Performance timing — performance.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

export interface NavigationTiming {
    /** Time to first byte. */
    timeToFirstByteMs: number;
    domInteractiveMs: number;
    domContentLoadedMs: number;
    loadMs: number;
    durationMs: number;
}

/**
 * A high-resolution timestamp, in milliseconds since the page started.
 *
 * Wrapped rather than passed through because `performance.now` needs `performance` as its `this`, and
 * a detached reference to it throws in some engines.
 */
export function now(): number {
    return performance.now();
}

/**
 * The navigation timing entry, flattened. Null before the entry exists — early enough in startup, or
 * in a browser without `getEntriesByType`.
 */
export function navigation(): NavigationTiming | null {
    const entries = performance.getEntriesByType
        ? performance.getEntriesByType("navigation") as PerformanceNavigationTiming[]
        : [];
    const e = entries && entries.length ? entries[0] : null;
    if (!e) {
        return null;
    }
    return {
        timeToFirstByteMs: e.responseStart,
        domInteractiveMs: e.domInteractive,
        domContentLoadedMs: e.domContentLoadedEventEnd,
        loadMs: e.loadEventEnd,
        durationMs: e.duration
    };
}
