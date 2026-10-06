// Rask default service worker (WASM) — the one SW a Rask WASM PWA needs. It does three jobs:
//   1. Offline app shell: a network-first runtime cache (fresh when online, cached when offline),
//      with navigations falling back to the cached page shell so deep links work offline. Files named
//      by their content hash are served cache-first instead.
//   2. Web Push: shows the pushed notification and focuses/opens a window on click (MDN's PushManager subscribed) —
//      shared with the Server SW via the imported rask-sw-shared handlers.
//   3. Background Sync: forwards a woken-up sync/periodicsync tag to the open clients (IBackgroundSync).
//      WASM-only, so it stays here rather than in the shared handlers.
//
// Registered by the page shell (see the WASM templates' / example's index.html), so an app reaches it with
// `await Navigator.ServiceWorker.Ready`. Bring your own SW by registering yours there instead.

// See the note in rask-sw-shared.ts: the webworker lib types `self` as the generic WorkerGlobalScope.
declare const self: ServiceWorkerGlobalScope & typeof globalThis;

// Replaces the @@RASK_SW@@ splice marker — imported for its side effects, which register the push
// and notificationclick listeners.
import "../../Rask.Core/Resources/rask-sw-shared.js";

const RASK_CACHE = "rask-cache-v1";

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

// A URL whose bytes can never change, so the cached copy is as good as the network's:
//   - /_rask/a/{hash}.css|js — a scoped-asset bundle, named by ScopedAssetBundle.IsContentHash's 12 hex;
//   - _framework/name.{fingerprint}.ext — the .NET SDK's fingerprint, 10 lowercase base-36 characters.
// Matched on the path's tail, so an app under a sub-path qualifies too. Deliberately strict, because a
// false positive is a stale app for ever: the fingerprint must carry a digit, which costs the odd
// all-letter one (about 4%) a round trip and keeps `_framework/my.extensions.wasm` out.
const RASK_SCOPED_ASSET = /\/_rask\/a\/[0-9a-f]{12}\.(?:css|js)$/;
const RASK_FINGERPRINTED = /\/_framework\/(?:[^/]+\/)*[^/]+\.(?=[a-z]*[0-9])[a-z0-9]{10}\.[A-Za-z0-9]+$/;

const raskIsContentAddressed = (pathname: string): boolean =>
    RASK_SCOPED_ASSET.test(pathname) || RASK_FINGERPRINTED.test(pathname);

const raskFetchAndStore = async (cache: Cache, req: Request): Promise<Response> => {
    const res = await fetch(req);
    if (res && res.ok) {
        cache.put(req, res.clone());
    }
    return res;
};

// Cache-first for content-addressed files — a repeat visit must not download the runtime again —
// and network-first with cache fallback for everything else. Only same-origin GETs are cached;
// cross-origin and non-GET requests pass straight through.
self.addEventListener("fetch", (event) => {
    const req = event.request;
    const url = new URL(req.url);
    if (req.method !== "GET" || url.origin !== self.location.origin) {
        return;
    }

    event.respondWith((async () => {
        const cache = await caches.open(RASK_CACHE);
        if (raskIsContentAddressed(url.pathname)) {
            return await cache.match(req) || raskFetchAndStore(cache, req);
        }

        try {
            return await raskFetchAndStore(cache, req);
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
