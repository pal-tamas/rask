// Drives the BUILT src/Rask.Wasm/Browser/rask-sw.js under Node with a stub `self`, `caches` and
// `fetch`, and reports — per request — what the worker answered with and how often it went to the
// network. Companion to ServiceWorkerCacheTests.cs.
//
// Usage:  node ServiceWorkerCacheFixture.mjs <path-to-rask-sw.js>
//
// Each case runs against a fresh worker and a fresh cache, so no case can be answered by a response
// an earlier one stored.
import {readFileSync} from "node:fs";

const workerPath = process.argv[2];
if (!workerPath) {
    console.error("usage: node ServiceWorkerCacheFixture.mjs <rask-sw.js path>");
    process.exit(2);
}

const ORIGIN = "https://app.test";
const workerSource = readFileSync(workerPath, "utf8");

interface StubResponse {
    ok: boolean;
    body: string;
    clone(): StubResponse;
}

interface StubRequest {
    url: string;
    method: string;
    mode: string;
}

interface Case {
    path: string;
    cached?: boolean;
    offline?: boolean;
    navigate?: boolean;
    /** A second cache entry, for the shell an offline navigation falls back to. */
    shell?: string;
}

const response = (body: string): StubResponse => ({ok: true, body, clone: () => response(body)});

async function run(c: Case) {
    const url = ORIGIN + c.path;
    const store = new Map<string, StubResponse>();
    if (c.cached) {
        store.set(url, response("cached"));
    }
    if (c.shell) {
        store.set(c.shell, response("shell"));
    }

    const key = (req: StubRequest | string) => typeof req === "string" ? req : req.url;
    const cache = {
        match: async (req: StubRequest | string) => store.get(key(req)),
        put: async (req: StubRequest, res: StubResponse) => void store.set(key(req), res),
    };

    let fetches = 0;
    const fetchStub = async (): Promise<StubResponse> => {
        fetches++;
        if (c.offline) {
            throw new TypeError("Failed to fetch");
        }
        return response("network");
    };

    const listeners = new Map<string, (event: unknown) => void>();
    const self = {
        location: {origin: ORIGIN},
        clients: {claim: async () => undefined, matchAll: async () => []},
        skipWaiting: async () => undefined,
        addEventListener: (type: string, listener: (event: unknown) => void) => void listeners.set(type, listener),
    };

    // The bundle is an IIFE that reads its three globals by bare name; handing them in as parameters
    // keeps one case's stubs out of the next one's.
    new Function("self", "caches", "fetch", workerSource)(self, {open: async () => cache}, fetchStub);

    const answers: Promise<StubResponse>[] = [];
    listeners.get("fetch")!({
        request: {url, method: "GET", mode: c.navigate ? "navigate" : "cors"},
        respondWith: (promise: Promise<StubResponse>) => void answers.push(promise),
    });

    const served = answers.length === 0
        ? "passed-through"
        : await answers[0].then((res) => res.body, () => "failed");

    return {served, fetches, stored: store.get(url)?.body ?? null};
}

const cases: Record<string, Case> = {
    frameworkCached: {path: "/_framework/Rask.Core.01r1zg2jrm.wasm", cached: true},
    frameworkUncached: {path: "/_framework/dotnet.native.nxw7lo0lh5.wasm"},
    frameworkUnderPathBase: {path: "/apps/shop/_framework/dotnet.runtime.zbexyp8zrs.js", cached: true},
    scopedAssetCached: {path: "/_rask/a/155e969457bd.js", cached: true},
    scopedAssetUnderPathBase: {path: "/apps/shop/_rask/a/3a2c681ce606.css", cached: true},

    indexHtml: {path: "/index.html", cached: true},
    mainJs: {path: "/main.js", cached: true},
    raskWasmJs: {path: "/rask.wasm.js", cached: true},
    serviceWorker: {path: "/rask-sw.js", cached: true},
    dotnetJsUnfingerprinted: {path: "/_framework/dotnet.js", cached: true},
    assemblyUnfingerprinted: {path: "/_framework/Foo.Extensions.wasm", cached: true},
    // Ten lowercase letters, exactly the fingerprint's length — the digit rule is what keeps it out.
    assemblyLowercaseTenLetters: {path: "/_framework/my.extensions.wasm", cached: true},
    fingerprintOutsideFramework: {path: "/css/site.01r1zg2jrm.css", cached: true},
    scopedAssetWrongHashLength: {path: "/_rask/a/155e9694.js", cached: true},
    scopedAssetSourceMap: {path: "/_rask/a/155e969457bd.js.map", cached: true},

    offlineCached: {path: "/main.js", cached: true, offline: true},
    offlineNavigation: {path: "/orders/42", offline: true, navigate: true, shell: "index.html"},
    offlineUncached: {path: "/main.js", offline: true},
};

const results: Record<string, unknown> = {};
for (const [name, c] of Object.entries(cases)) {
    results[name] = await run(c);
}

process.stdout.write(JSON.stringify(results) + "\n");
