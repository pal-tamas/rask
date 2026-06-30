// Rask default service worker (WASM) — the one SW a Rask WASM PWA needs. It does two jobs:
//   1. Offline app shell: a network-first runtime cache (fresh when online, cached when offline),
//      with navigations falling back to the cached page shell so deep links work offline.
//   2. Web Push: shows the pushed notification and focuses/opens a window on click (IWebPush) —
//      shared with the Server SW via the spliced rask-sw-shared.js handlers below.
//
// This file is generated: Resources/rask-sw.js (this template) has the Core shared handlers spliced
// in at the marker below, and the assembled result is written to Browser/rask-sw.js (the served,
// tracked artifact). Edit this template, not Browser/rask-sw.js.
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

// @@RASK_SW@@
