// Shared Rask service-worker handlers, spliced into both the WASM service worker
// (src/Rask.Wasm/Resources/rask-sw.js) and the Server service worker
// (src/Rask.Server/Resources/rask-sw.js) at their shared-handlers splice marker. These two handlers
// are transport-neutral — a push and its click behave identically whether the app is a WASM PWA or a
// Server app — so they live here once. Each host's SW adds its own caching/offline strategy around
// them (WASM: offline app-shell cache; Server: offline-fallback page), which is the only divergence.
//
// NB: no regex literals here — the MSBuild client-JS splice mangles backslashes (same constraint as
// the spliced client helpers).

// A push arrived: parse the (optional) JSON payload and show a notification. The payload shape matches
// what Rask.WebPush's WebPushMessage serializes, and what INotifications/IWebPush document.
self.addEventListener("push", (event) => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch (_) {
        data = {body: event.data ? event.data.text() : ""};
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
    const url = (event.notification.data && event.notification.data.url) || "/";
    event.waitUntil(
        self.clients.matchAll({type: "window", includeUncontrolled: true}).then((clients) => {
            for (let i = 0; i < clients.length; i++) {
                if (clients[i].url === url && "focus" in clients[i]) {
                    return clients[i].focus();
                }
            }
            return self.clients.openWindow ? self.clients.openWindow(url) : undefined;
        })
    );
});
