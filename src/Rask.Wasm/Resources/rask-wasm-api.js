// WASM-only framework Web-API helpers, spliced into rask.wasm.js ONLY (by the RASK_WASM_API marker).
// These back APIs that can't work on the Server transport, so they must not ship in the Server
// client (rask.js) — keeping the Core shared rask-api.js to genuinely-shared helpers only.

// Web Push (driven by IWebPush in Rask.Wasm.Browser). Push needs a Service Worker registration plus
// key (de)serialization that IJSRuntime can't express directly, so it all lives here.
window.__raskPush = window.__raskPush || {
    isSupported: () =>
        ("serviceWorker" in navigator) && ("PushManager" in window) && ("Notification" in window),

    requestPermission: () => Notification.requestPermission(),

    register: (swUrl) => navigator.serviceWorker.register(swUrl).then(() => undefined),

    subscribe: async (vapidPublicKey) => {
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
    _serialize: (sub) => ({
        endpoint: sub.endpoint,
        expirationTime: sub.expirationTime,
        p256dh: window.__raskPush._b64url(sub.getKey("p256dh")),
        auth: window.__raskPush._b64url(sub.getKey("auth"))
    }),

    // NB: no regex literals here — the MSBuild client-JS splice mangles backslashes, so base64url
    // (de)coding uses split/join instead of regex replace patterns.
    _b64url: (buf) => {
        if (!buf) return "";
        const bytes = new Uint8Array(buf);
        let s = "";
        for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
        let out = btoa(s).split("+").join("-").split("/").join("_");
        while (out.length > 0 && out[out.length - 1] === "=") out = out.slice(0, -1);
        return out;
    },

    _urlB64ToBytes: (base64) => {
        const pad = "=".repeat((4 - base64.length % 4) % 4);
        const norm = (base64 + pad).split("-").join("+").split("_").join("/");
        const raw = atob(norm);
        const out = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
        return out;
    }
};
