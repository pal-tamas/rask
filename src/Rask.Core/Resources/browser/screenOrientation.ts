// Screen orientation — screen.orientation.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

export interface OrientationInfo {
    /** "portrait-primary", "landscape-secondary", and so on. */
    type: string;
    /** Degrees clockwise from the device's natural orientation. */
    angle: number;
}

export function isSupported(): boolean {
    return typeof screen !== "undefined" && "orientation" in screen;
}

/** A plain snapshot of the live ScreenOrientation object. */
export function current(): OrientationInfo {
    return {type: screen.orientation.type, angle: screen.orientation.angle};
}

/**
 * Lock the orientation.
 *
 * Only works while the document is FULLSCREEN — outside it the promise rejects, which is why Rask's
 * own gesture component enters fullscreen first. Mobile only in practice.
 */
export function lock(type: OrientationLockType): Promise<void> {
    return screen.orientation.lock(type);
}

export function unlock(): void {
    screen.orientation.unlock();
}
