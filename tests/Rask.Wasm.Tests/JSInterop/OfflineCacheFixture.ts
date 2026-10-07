// Node-driven fixture for what the WASM service worker keeps offline
// (src/Rask.Wasm/Resources/rask-offline-cache.ts — keptOffline), the rule its fetch handler applies
// before every cache.put.
//
// The bug (#1184): the worker stored EVERY successful same-origin GET, so a signed-in visitor's
// `/api/…` answers sat in Cache Storage after sign-out and were replayed, offline, to whoever used the
// browser next. A fetch event cannot be raised outside a browser, so the decision is a pure function
// and this asks it about each kind of request by name.

import { keptOffline, RASK_CACHE } from "../../../src/Rask.Wasm/Resources/rask-offline-cache.js";

interface Case {
    url: string;
    mode?: string;
    destination?: string;
    status?: number;
    cacheControl?: string;
}

// A browser stamps `mode` and `destination` itself and refuses "navigate" from script, so the two
// objects are stated rather than constructed.
function ask(c: Case): boolean {
    const status = c.status ?? 200;
    const request = { url: "https://app.test" + c.url, mode: c.mode ?? "cors", destination: c.destination ?? "" };
    const response = {
        ok: status >= 200 && status < 300,
        headers: new Headers(c.cacheControl ? { "Cache-Control": c.cacheControl } : {}),
    };

    return keptOffline(request as Request, response as Response);
}

const cases: Record<string, Case> = {
    navigation: { url: "/orders/42", mode: "navigate", destination: "document" },
    script: { url: "/app.js", destination: "script" },
    style: { url: "/app.css", destination: "style" },
    image: { url: "/logo.svg", destination: "image" },
    runtime: { url: "/_framework/dotnet.native.wasm" },
    runtimeUnderSubPath: { url: "/shop/_framework/Shop.wasm" },
    islandManifest: { url: "/_rask/external/manifest.json" },
    api: { url: "/api/orders" },
    apiMarkedPublic: { url: "/api/catalog", cacheControl: "public, max-age=60" },
    apiMarkedPrivate: { url: "/api/me", cacheControl: "private, max-age=60" },
    navigationMarkedNoStore: { url: "/account", mode: "navigate", destination: "document", cacheControl: "No-Store" },
    scriptMarkedPrivate: { url: "/me.js", destination: "script", cacheControl: "private" },
    failedNavigation: { url: "/missing", mode: "navigate", destination: "document", status: 404 },
};

const kept: Record<string, boolean> = {};
for (const name of Object.keys(cases)) {
    kept[name] = ask(cases[name]);
}

process.stdout.write(JSON.stringify({ cache: RASK_CACHE, kept }) + "\n");
