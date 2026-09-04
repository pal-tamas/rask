// Screen and display info — window.screen plus devicePixelRatio.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

export interface ScreenInfo {
    width: number;
    height: number;
    /** Excludes OS furniture — taskbar, dock, menu bar. */
    availWidth: number;
    availHeight: number;
    colorDepth: number;
    /** Physical pixels per CSS pixel: 2 or 3 on a retina display, and not always an integer. */
    pixelRatio: number;
}

/** A plain snapshot of the display. */
export function info(): ScreenInfo {
    return {
        width: screen.width,
        height: screen.height,
        availWidth: screen.availWidth,
        availHeight: screen.availHeight,
        colorDepth: screen.colorDepth,
        pixelRatio: window.devicePixelRatio
    };
}
