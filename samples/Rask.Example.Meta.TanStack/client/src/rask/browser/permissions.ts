// Permissions — navigator.permissions.
//
// See ./geolocation.ts for the two rules every module in this directory follows.

/** Whether this browser exposes the Permissions API. */
export function isSupported(): boolean {
    return typeof navigator !== "undefined" && !!navigator.permissions;
}

/**
 * The current state of one permission, without prompting for it.
 *
 * `navigator.permissions.query` resolves to a live PermissionStatus whose `.state` changes in place;
 * this returns just the string, which is the part a caller can act on and the only part that
 * serializes.
 */
export function query(name: PermissionName): Promise<PermissionState> {
    return navigator.permissions.query({name}).then((status) => status.state);
}
