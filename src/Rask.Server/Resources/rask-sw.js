// Rask default service worker (Server) — the SW a Rask Server PWA needs, served at {PathBase}/rask-sw.js
// only when PWA is opted into (AddRaskPwa). It does two jobs:
//   1. Web Push: shows the pushed notification and focuses/opens a window on click (IWebPush) — the
//      shared rask-sw-shared.js handlers spliced in at the marker below.
//   2. Offline fallback: when a navigation fails offline, serve a static offline page.
//
// CRITICAL: unlike the WASM SW, this MUST NOT cache navigations / replay an app shell. A Server page is
// server-rendered HTML carrying a one-shot session id (data-rask-root) and served `Cache-Control:
// no-store`; caching it would violate that contract, could replay one principal's session id to another,
// and is useless anyway (the live app needs the WebSocket — there is no client-side router to take over
// offline). So we cache ONLY a static offline.html and serve it for failed navigations. Everything else
// passes straight through to the network.
//
// The offline page is resolved relative to the SW's own scope ({PathBase}/), so no base-path injection is
// needed: under a sub-path deploy it still points at {PathBase}/offline.html.

const RASK_OFFLINE_CACHE = "rask-offline-v1";
const OFFLINE_URL = new URL("offline.html", self.registration.scope).href;

self.addEventListener("install", (event) => event.waitUntil(
    caches.open(RASK_OFFLINE_CACHE).then((cache) => cache.add(OFFLINE_URL)).then(() => self.skipWaiting())
));

self.addEventListener("activate", (event) => event.waitUntil(
    // Drop stale offline caches from older SW versions, then take control of open clients.
    caches.keys()
        .then((keys) => Promise.all(keys.filter((k) => k !== RASK_OFFLINE_CACHE).map((k) => caches.delete(k))))
        .then(() => self.clients.claim())
));

// Only intercept navigations: if the network is unreachable, show the static offline page. Assets, the
// WebSocket, and everything else are never cached or rewritten — the live app drives them over the socket.
self.addEventListener("fetch", (event) => {
    if (event.request.mode !== "navigate") {
        return;
    }
    event.respondWith(fetch(event.request).catch(() => caches.match(OFFLINE_URL)));
});

// @@RASK_SW@@
