// Element enters or leaves the viewport — IntersectionObserver.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

export interface IntersectionChange {
    isIntersecting: boolean;
    /** How much of the element is visible, 0–1. */
    ratio: number;
}

export interface IntersectionOptions {
    /**
     * Ratios at which to report. Omitted means a single threshold of 0 — fire when any part of the
     * element crosses the boundary, which is what lazy-loading and infinite scroll want.
     */
    thresholds?: number[] | null;
    /** Grows or shrinks the root's box, CSS-margin syntax. "200px" fires 200px before it is visible. */
    rootMargin?: string | null;
}

/** Observe an element against the viewport. Returns the stop function. */
export function observe(
    element: Element,
    onChange: (change: IntersectionChange) => void,
    options?: IntersectionOptions): () => void {
    const init: IntersectionObserverInit = {
        threshold: (options && options.thresholds && options.thresholds.length) ? options.thresholds : 0
    };
    if (options && options.rootMargin) {
        init.rootMargin = options.rootMargin;
    }

    const observer = new IntersectionObserver((entries) => {
        for (let i = 0; i < entries.length; i++) {
            const e = entries[i];
            onChange({isIntersecting: e.isIntersecting, ratio: e.intersectionRatio});
        }
    }, init);
    observer.observe(element);

    let stopped = false;
    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        observer.disconnect();
    };
}
