// The count on the app icon — the Badging API.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Only visible on an INSTALLED app: in a browser tab there is no icon to badge, and the calls
// succeed while showing nothing.

export function isSupported(): boolean {
    return typeof navigator !== "undefined" && "setAppBadge" in navigator;
}

/**
 * Set the badge. With a count it shows the number; with nothing it shows a plain dot, which is what
 * you want for "something is waiting" without claiming to have counted it.
 *
 * Browsers clamp large numbers to something like "99+" on their own.
 */
export function set(count?: number | null): Promise<void> {
    return (count === null || count === undefined)
        ? navigator.setAppBadge()
        : navigator.setAppBadge(count);
}

export function clear(): Promise<void> {
    return navigator.clearAppBadge();
}
