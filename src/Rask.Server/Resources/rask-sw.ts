// Rask default service worker (Server) — the SW a Rask Server PWA needs, served at {PathBase}/rask-sw.js
// only when PWA is opted into (AddRaskPwa). It does two jobs:
//   1. Web Push: shows the pushed notification and focuses/opens a window on click (IWebPush) — the
//      shared rask-sw-shared handlers, imported below.
//   2. Offline fallback: when a navigation fails offline, serve a static offline page.
//
// CRITICAL: unlike the WASM SW, this MUST NOT cache navigations / replay an app shell. A page that keeps
// a live session is server-rendered HTML carrying a one-shot session id (data-rask-root) and served
// `Cache-Control: no-store`; caching it would violate that contract, could replay one principal's session
// id to another, and is useless anyway (the live app needs the WebSocket — there is no client-side router
// to take over offline). So we cache ONLY a static offline.html and serve it for failed navigations.
//
// Note that a page needing nothing live may now be served WITHOUT a session and with an ordinary
// `private` cache policy (RaskServerOptions.StaticPages). That does not change this file's job — the HTTP
// cache handles those, not us — but if anyone ever teaches this SW to cache navigations, it MUST key on
// the Cache-Control the server actually sent. The Cache Storage API honours nothing automatically and
// would happily store a page that was never meant to outlive its request.
//
// The offline page is resolved relative to the SW's own scope ({PathBase}/), so no base-path injection is
// needed: under a sub-path deploy it still points at {PathBase}/offline.html.

// See the note in rask-sw-shared.ts: the webworker lib types `self` as the generic WorkerGlobalScope,
// where `registration`, `clients` and `skipWaiting` do not exist.
declare const self: ServiceWorkerGlobalScope & typeof globalThis;

// The push + notificationclick handlers, shared with the WASM worker. Imported for side effects —
// it registers its own listeners. This replaces the @@RASK_SW@@ splice marker: the dependency is now
// stated in the file that has it rather than assembled by an MSBuild string replace.
import "../../Rask.Core/Resources/rask-sw-shared.js";

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
    // Fall back to the HTTP cache BEFORE the offline page. The SW is consulted ahead of the HTTP cache
    // for navigations, so going straight to offline.html would bury a perfectly good cached copy of the
    // very page the user asked for — which is exactly what a cacheable static page leaves behind.
    event.respondWith(
        fetch(event.request).catch(async () => {
            const cached = await caches.match(event.request) ?? await caches.match(OFFLINE_URL);

            // Both matches resolve to undefined when there is no cached copy AND the install-time add
            // failed (offline on first load, or no offline.html deployed). respondWith requires a
            // Response, so answer with a real one rather than letting the promise reject into an
            // opaque network error.
            return cached ?? new Response(
                "Offline.",
                { status: 503, headers: { "Content-Type": "text/plain" } });
        }));
});

export {};
