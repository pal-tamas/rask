// Web Push subscriptions — PushManager, through a service worker.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// The other module in this layer whose server half is ours: a subscription is only useful once
// something signs and encrypts a message for it, which is `Rask.WebPush` (VAPID, RFC 8291).
//
// Two conversions make this worth a module rather than three lines at a call site. The VAPID public
// key arrives as base64url text and `applicationServerKey` wants bytes; and a live PushSubscription
// serializes through `toJSON()` into a NESTED shape ({endpoint, keys: {p256dh, auth}}) while every
// backend, Rask's included, binds a FLAT one. Posting the nested shape is the classic Web Push bug:
// the server answers 204, stores a subscription with two null keys, and every send afterwards fails to
// encrypt for it.

/** A subscription, flattened and ready to POST to your backend. */
export interface PushSubscriptionInfo {
    endpoint: string;
    /** Milliseconds since the epoch, or null — almost always null in practice. */
    expirationTime: number | null;
    /** base64url. */
    p256dh: string;
    /** base64url. */
    auth: string;
}

function toBase64Url(buf: ArrayBuffer | null): string {
    if (!buf) {
        return "";
    }
    const bytes = new Uint8Array(buf);
    let s = "";
    for (let i = 0; i < bytes.length; i++) {
        s += String.fromCharCode(bytes[i]);
    }
    let out = btoa(s).split("+").join("-").split("/").join("_");
    while (out.length > 0 && out[out.length - 1] === "=") {
        out = out.slice(0, -1);
    }
    return out;
}

// Uint8Array<ArrayBuffer> rather than a bare Uint8Array: applicationServerKey is a BufferSource, and
// the type is generic over its backing buffer — a view that might be over SharedArrayBuffer is not one.
function fromBase64Url(base64: string): Uint8Array<ArrayBuffer> {
    const pad = "=".repeat((4 - base64.length % 4) % 4);
    const norm = (base64 + pad).split("-").join("+").split("_").join("/");
    const raw = atob(norm);
    const out = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) {
        out[i] = raw.charCodeAt(i);
    }
    return out;
}

function serialize(sub: PushSubscription): PushSubscriptionInfo {
    return {
        endpoint: sub.endpoint,
        expirationTime: sub.expirationTime,
        p256dh: toBase64Url(sub.getKey("p256dh")),
        auth: toBase64Url(sub.getKey("auth"))
    };
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined"
        && ("serviceWorker" in navigator)
        && ("PushManager" in window)
        && ("Notification" in window);
}

/**
 * Ask for notification permission. Push requires it: a subscription is only granted with
 * `userVisibleOnly`, so a browser will not let you push silently.
 */
export function requestPermission(): Promise<NotificationPermission> {
    return Notification.requestPermission();
}

/** Register the service worker that will receive pushes. */
export function register(serviceWorkerUrl: string): Promise<void> {
    return navigator.serviceWorker.register(serviceWorkerUrl).then(() => undefined);
}

/**
 * Subscribe, and return the flat shape to POST to your backend.
 *
 * Waits on `serviceWorker.ready`, so register first — this resolves only once a worker is actually
 * controlling the page.
 */
export async function subscribe(vapidPublicKey: string): Promise<PushSubscriptionInfo> {
    const reg = await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: fromBase64Url(vapidPublicKey)
    });
    return serialize(sub);
}

/** The existing subscription, or null. */
export async function getSubscription(): Promise<PushSubscriptionInfo | null> {
    const reg = await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.getSubscription();
    return sub ? serialize(sub) : null;
}

/** Unsubscribe. False when there was nothing subscribed. */
export async function unsubscribe(): Promise<boolean> {
    const reg = await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.getSubscription();
    return sub ? await sub.unsubscribe() : false;
}
