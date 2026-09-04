// Local notifications — the Notifications API.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// This shows a notification from the PAGE, and only while the page is open. For one that arrives when
// it is not, you want ./webPush.ts and a service worker.

export function isSupported(): boolean {
    return typeof window !== "undefined" && "Notification" in window;
}

/** "default" (not yet asked), "granted" or "denied". */
export function permission(): NotificationPermission {
    return Notification.permission;
}

/**
 * Ask for permission. Chrome and Firefox require a user gesture; asking on page load is both refused
 * and a good way to be denied permanently.
 */
export function requestPermission(): Promise<NotificationPermission> {
    return Notification.requestPermission();
}

/** Show one. A no-op if permission has not been granted — the platform silently ignores it. */
export function show(title: string, options?: NotificationOptions): void {
    new Notification(title, options || {});
}
