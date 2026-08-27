// Rask default service worker (WASM) — the one SW a Rask WASM PWA needs. It does three jobs:
//   1. Offline app shell: a network-first runtime cache (fresh when online, cached when offline),
//      with navigations falling back to the cached page shell so deep links work offline.
//   2. Web Push: shows the pushed notification and focuses/opens a window on click (IWebPush) —
//      shared with the Server SW via the imported rask-sw-shared handlers.
//   3. Background Sync: forwards a woken-up sync/periodicsync tag to the open clients (IBackgroundSync).
//      WASM-only, so it stays here rather than in the shared handlers.
//
// Registered by the page shell (see the WASM templates' / example's index.html) or by
// IWebPush.RegisterServiceWorkerAsync(). Bring your own SW (pass a URL) to customize.

// See the note in rask-sw-shared.ts: the webworker lib types `self` as the generic WorkerGlobalScope.
declare const self: ServiceWorkerGlobalScope & typeof globalThis;

// Replaces the @@RASK_SW@@ splice marker — imported for its side effects, which register the push
// and notificationclick listeners.
import "../../Rask.Core/Resources/rask-sw-shared.js";

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

// Background Sync (driven by IBackgroundSync). Deliberately NOT in rask-sw-shared.ts: a Server app
// renders over a WebSocket and has no client-side runtime to hand a woken-up event to, so shipping this
// handler in the Server SW would advertise a capability that cannot fire there.
//
// The browser's guarantee is that "sync" fires once connectivity returns even if the tab is CLOSED. What
// Rask can offer is narrower, and the gap is the part that matters: the .NET runtime lives in the page,
// not in this worker, so C# runs only while a client is alive. The handler therefore forwards the tag to
// every open client and resolves. With no client open the registration is consumed unseen — which is
// exactly why IBackgroundSync tells you to re-request your tags at boot.

/**
 * The shape both sync events share. Neither is in lib.webworker yet, so the tag is stated here
 * rather than asserted at each call site.
 */
interface SyncLikeEvent extends ExtendableEvent {
    readonly tag: string;
}

const raskForwardSync = (event: SyncLikeEvent, kind: "sync" | "periodicsync"): void => event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
        for (const client of clients) {
            client.postMessage({ rask: kind, tag: event.tag });
        }
    })
);

// Cast at the boundary: lib.webworker declares neither "sync" nor "periodicsync" in its event map,
// so the listener's argument arrives as a bare Event. Confined to these two lines.
self.addEventListener("sync", (event) => raskForwardSync(event as SyncLikeEvent, "sync"));
self.addEventListener("periodicsync", (event) => raskForwardSync(event as SyncLikeEvent, "periodicsync"));

export {};
