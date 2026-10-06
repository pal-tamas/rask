// What the WASM service worker keeps for offline use, and under which name — shared by the worker that
// fills the cache (rask-sw.ts) and the page that empties it on sign-out (rask-wasm-api.ts).

export const RASK_CACHE = "rask-cache-v1";

// What a page is built from, as the browser itself labels the request.
const SHELL_DESTINATIONS = new Set(["document", "script", "style", "font", "image", "manifest", "worker"]);

// The runtime and the framework's own files are fetched by script (an empty destination), so they are
// recognised by where they live. `includes`, not `startsWith`: an app can be deployed under a sub-path.
const SHELL_PATHS = ["/_framework/", "/_rask/", "/_content/"];

/**
 * Whether a response belongs in the offline cache.
 *
 * The cache is the app SHELL: the page, its scripts and styles, the .NET runtime. It is not a copy of
 * what the signed-in visitor was shown — the Cache Storage API honours no header on its own, so an
 * authenticated `/api/…` answer stored here outlives the sign-out and is replayed, offline, to whoever
 * opens the browser next. So a response the server marked `no-store` or `private` is never kept, and
 * anything that is not part of the shell is kept only when the server said it is `public`.
 */
export function keptOffline(request: Request, response: Response): boolean {
    if (!response.ok) {
        return false;
    }

    const control = (response.headers.get("Cache-Control") ?? "").toLowerCase();
    if (/\b(no-store|private)\b/.test(control)) {
        return false;
    }

    if (request.mode === "navigate" || SHELL_DESTINATIONS.has(request.destination)) {
        return true;
    }

    const path = new URL(request.url).pathname;
    return SHELL_PATHS.some((prefix) => path.includes(prefix)) || /\bpublic\b/.test(control);
}
