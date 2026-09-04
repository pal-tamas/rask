// Screen orientation — screen.orientation.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

/**
 * The lock half of the Screen Orientation API.
 *
 * Declared locally for the same reason as the rest of this directory's vendor shapes: `lock` and its
 * `OrientationLockType` are recent additions to `lib.dom`, and a consumer whose TypeScript predates
 * them gets a type error inside Rask's code rather than in their own. Measured on a scaffolded Next
 * app.
 */
type OrientationLock =
    | 'any'
    | 'natural'
    | 'landscape'
    | 'portrait'
    | 'portrait-primary'
    | 'portrait-secondary'
    | 'landscape-primary'
    | 'landscape-secondary';

interface ScreenOrientationWithLock {
    lock(orientation: OrientationLock): Promise<void>;
}


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
export function lock(type: OrientationLock): Promise<void> {
    return (screen.orientation as unknown as ScreenOrientationWithLock).lock(type);
}

export function unlock(): void {
    screen.orientation.unlock();
}
