// Transport-agnostic PWA framework helpers, imported by BOTH the Server client (rask.ts) and the WASM
// client (rask.wasm.ts). These back the PWA browser APIs that work on either transport — IWebPush
// (subscribe), INotifications, IBadge, IWakeLock (all in Rask.Core.Browser). The WASM-only helpers that
// need transient activation / a static SW instance (the manifest injector and install-prompt capture)
// and the device APIs stay in Rask.Wasm's rask-wasm-api.ts.
//
// Imported for its side effects: everything here is published on `window` because .NET reaches it by
// dotted name, and an IJSRuntime identifier is resolved against `window` at call time. Their shapes are
// declared in rask-window.d.ts.
//
// The base64url helpers still use split/join rather than a regex. That was originally forced — the
// MSBuild splice mangled backslashes — and the constraint is gone now that esbuild parses the file
// properly, but a working codec is not worth rewriting to prove a point.

// Web Push (driven by IWebPush). Push needs a Service Worker registration plus key (de)serialization
// that IJSRuntime can't express directly, so it all lives here. The SW URL is resolved by C#
// (IWebPush.RegisterServiceWorkerAsync defaults to {PathBase}/rask-sw.js — the WASM boot asset or the
// Server AddRaskPwa endpoint).
window.__raskPush = window.__raskPush || {
    isSupported: () =>
        ("serviceWorker" in navigator) && ("PushManager" in window) && ("Notification" in window),

    requestPermission: () => Notification.requestPermission(),

    register: (swUrl: string) => navigator.serviceWorker.register(swUrl).then(() => undefined),

    subscribe: async (vapidPublicKey: string) => {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: window.__raskPush._urlB64ToBytes(vapidPublicKey)
        });
        return window.__raskPush._serialize(sub);
    },

    getSubscription: async () => {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        return sub ? window.__raskPush._serialize(sub) : null;
    },

    unsubscribe: async () => {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        return sub ? await sub.unsubscribe() : false;
    },

    // Shape a live PushSubscription into the C# PushSubscription record (base64url key bytes).
    _serialize: (sub: PushSubscription) => ({
        endpoint: sub.endpoint,
        expirationTime: sub.expirationTime,
        p256dh: window.__raskPush._b64url(sub.getKey("p256dh")),
        auth: window.__raskPush._b64url(sub.getKey("auth"))
    }),

    _b64url: (buf: ArrayBuffer | null) => {
        if (!buf) return "";
        const bytes = new Uint8Array(buf);
        let s = "";
        for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
        let out = btoa(s).split("+").join("-").split("/").join("_");
        while (out.length > 0 && out[out.length - 1] === "=") out = out.slice(0, -1);
        return out;
    },

    _urlB64ToBytes: (base64: string) => {
        const pad = "=".repeat((4 - base64.length % 4) % 4);
        const norm = (base64 + pad).split("-").join("+").split("_").join("/");
        const raw = atob(norm);
        const out = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
        return out;
    }
};

// Local notifications (driven by INotifications). `new Notification(...)` is a constructor IJSRuntime
// can't call directly, so showing goes through here. Permission read/request are plain calls in C#.
window.__raskNotify = window.__raskNotify || {
    isSupported: () => "Notification" in window,
    show: (title: string, options?: NotificationOptions) => {
        new Notification(title, options || {});
    }
};

// App badging (driven by IBadge). setAppBadge() with no argument shows a generic dot, with a number
// shows the count — collapse the C# nullable int to that here. clearAppBadge() removes it.
window.__raskBadge = window.__raskBadge || {
    isSupported: () => "setAppBadge" in navigator,
    set: (count: number | null | undefined) => (count === null || count === undefined)
        ? navigator.setAppBadge()
        : navigator.setAppBadge(count),
    clear: () => navigator.clearAppBadge()
};

// Screen Wake Lock (driven by IWakeLock). A WakeLockSentinel is a live object IJSRuntime can't return,
// so it's kept here under an integer id. Browsers auto-release the lock when the page is hidden, so we
// re-acquire still-held locks when the page becomes visible again — a C# sentinel stays effective until
// it's disposed (which calls release).
window.__raskWakeLock = window.__raskWakeLock || (() => {
    /** One live lock, and whether the browser has taken it away behind our back. */
    interface WakeLockEntry {
        sentinel: WakeLockSentinel;
        released: boolean;
    }

    const held = new Map<number, WakeLockEntry>();
    let nextId = 1;
    let visBound = false;

    const track = (entry: WakeLockEntry): void => {
        entry.sentinel.addEventListener("release", () => { entry.released = true; });
    };

    const bindVisibility = (): void => {
        if (visBound) return;
        visBound = true;
        document.addEventListener("visibilitychange", async () => {
            if (document.visibilityState !== "visible") return;
            for (const entry of held.values()) {
                if (!entry.released) continue;
                try {
                    entry.sentinel = await navigator.wakeLock.request("screen");
                    entry.released = false;
                    track(entry);
                } catch {
                    // Best-effort re-acquire: the page may have lost the right to hold one.
                }
            }
        });
    };

    return {
        isSupported: () => "wakeLock" in navigator,
        request: async () => {
            bindVisibility();
            const entry: WakeLockEntry = {
                sentinel: await navigator.wakeLock.request("screen"),
                released: false
            };
            track(entry);
            const id = nextId++;
            held.set(id, entry);
            return id;
        },
        release: async (id: number) => {
            const entry = held.get(id);
            if (!entry) return;
            held.delete(id);
            try {
                await entry.sentinel.release();
            } catch {
                // Already released (e.g. by the page going hidden).
            }
        }
    };
})();

export {};
