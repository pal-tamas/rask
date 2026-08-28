// Shared Rask service-worker handlers, imported by both the WASM service worker
// (src/Rask.Wasm/Resources/rask-sw.ts) and the Server one (src/Rask.Server/Resources/rask-sw.ts).
// These two handlers are transport-neutral — a push and its click behave identically whether the app
// is a WASM PWA or a Server app — so they live here once. Each host's worker adds its own
// caching/offline strategy around them (WASM: offline app-shell cache; Server: offline-fallback
// page), which is the only divergence.
//
// Imported for its side effects: `import "./rask-sw-shared.js"` registers both listeners. There is
// nothing to export, and inventing an `install()` to call would only add a step a host could forget.

// `self` inside a service worker is a ServiceWorkerGlobalScope, but TypeScript cannot know that from
// the file alone — with the webworker lib it types `self` as the generic WorkerGlobalScope, where
// `registration`, `clients` and the push events do not exist. Stating it is the standard way, and it
// is what makes `event.data` on a PushEvent resolve at all.
declare const self: ServiceWorkerGlobalScope & typeof globalThis;

/** The payload shape Rask.WebPush's WebPushMessage serializes, and what INotifications documents. */
interface RaskPushPayload {
    title?: string;
    body?: string;
    icon?: string;
    badge?: string;
    tag?: string;
    data?: { url?: string } & Record<string, unknown>;
}

// A push arrived: parse the (optional) JSON payload and show a notification.
self.addEventListener("push", (event) => {
    let data: RaskPushPayload = {};
    try {
        data = event.data ? (event.data.json() as RaskPushPayload) : {};
    } catch {
        // A push body is whatever the sender chose to put there, so a non-JSON one is a normal
        // event rather than a fault — show it as plain text instead of dropping the notification.
        data = { body: event.data ? event.data.text() : "" };
    }

    const title = data.title || "Notification";
    event.waitUntil(self.registration.showNotification(title, {
        body: data.body,
        icon: data.icon,
        badge: data.badge,
        tag: data.tag,
        data: data.data || {}
    }));
});

// Notification clicked: focus an existing window for the target URL, else open one.
self.addEventListener("notificationclick", (event) => {
    event.notification.close();

    const payload = event.notification.data as RaskPushPayload["data"];
    const url = payload?.url || "/";

    event.waitUntil(
        self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
            for (const client of clients) {
                if (client.url === url && "focus" in client) {
                    return client.focus();
                }
            }

            return self.clients.openWindow ? self.clients.openWindow(url) : undefined;
        })
    );
});

// Marks the file as a module rather than a script. Without it every declaration above would be
// global, which is both wrong and what makes the `self` redeclaration above a duplicate-identifier
// error instead of a local narrowing.
export {};
