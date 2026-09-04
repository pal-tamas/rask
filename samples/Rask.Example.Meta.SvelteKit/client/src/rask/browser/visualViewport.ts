// Visual viewport — window.visualViewport.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// This is the viewport the user can actually see, which is not the layout viewport once a soft
// keyboard opens or the user pinch-zooms — the difference is what lets you keep an input above the
// keyboard instead of under it.

export interface VisualViewportSnapshot {
    width: number;
    height: number;
    offsetLeft: number;
    offsetTop: number;
    pageLeft: number;
    pageTop: number;
    scale: number;
}

export function isSupported(): boolean {
    return typeof window !== "undefined" && !!window.visualViewport;
}

/** A plain snapshot of the live VisualViewport object, or null where unsupported. */
export function current(): VisualViewportSnapshot | null {
    const v = typeof window === "undefined" ? null : window.visualViewport;
    if (!v) {
        return null;
    }
    return {
        width: v.width,
        height: v.height,
        offsetLeft: v.offsetLeft,
        offsetTop: v.offsetTop,
        pageLeft: v.pageLeft,
        pageTop: v.pageTop,
        scale: v.scale
    };
}
