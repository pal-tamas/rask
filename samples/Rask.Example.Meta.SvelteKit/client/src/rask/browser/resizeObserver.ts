// Element size changes — ResizeObserver.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// The reason to reach for this rather than a window resize listener: it fires when the ELEMENT
// changes size, including when a sibling's content pushed it around, and it does not fire for a
// window resize that left the element alone.

export interface ContentRect {
    width: number;
    height: number;
}

/**
 * Observe an element's content box. Returns the stop function.
 *
 * The callback receives one call per entry per batch, which is how the observer reports it — an
 * element resized twice in a frame is one notification, not two.
 */
export function observe(element: Element, onResize: (rect: ContentRect) => void): () => void {
    const observer = new ResizeObserver((entries) => {
        for (let i = 0; i < entries.length; i++) {
            const r = entries[i].contentRect;
            onResize({width: r.width, height: r.height});
        }
    });
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
