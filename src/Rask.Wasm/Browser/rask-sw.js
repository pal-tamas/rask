// Rask default service worker — the one SW a Rask WASM PWA needs. It does two jobs:
//   1. Offline app shell: a network-first runtime cache (fresh when online, cached when offline),
//      with navigations falling back to the cached page shell so deep links work offline.
//   2. Web Push: shows the pushed notification and focuses/opens a window on click (IWebPush).
//
// Registered by the page shell (see the WASM templates' / example's index.html) or by
// IWebPush.RegisterServiceWorkerAsync(). Bring your own SW (pass a URL) to customize.

const RASK_CACHE = "rask-cache-v1";

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

// Network-first with cache fallback. Only same-origin GETs are cached; cross-origin and
// non-GET requests pass straight through.
self.addEventListener("fetch", (event) => {
    const req = event.request;
    if (req.method !== "GET" || new URL(req.url).origin !== self.location.origin) {
        return;
    }
    event.respondWith((async () => {
        const cache = await caches.open(RASK_CACHE);
        try {
            const res = await fetch(req);
            if (res && res.ok) {
                cache.put(req, res.clone());
            }
            return res;
        } catch (err) {
            const cached = await cache.match(req);
            if (cached) {
                return cached;
            }
            // Offline navigation: fall back to the cached app shell so client-side routes render.
            if (req.mode === "navigate") {
                const shell = await cache.match("index.html") || await cache.match("./");
                if (shell) {
                    return shell;
                }
            }
            throw err;
        }
    })());
});

// A push arrived: parse the (optional) JSON payload and show a notification.
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
