// Dev-only "hot reload applied" indicator — one implementation, imported by both transports
// (Server rask.ts, WASM rask.wasm.ts).
//
// It lives here rather than in each host because the transports differ in how the *notification*
// arrives — Server pushes a {"type":"hotReload"} frame over the socket, WASM calls the exported
// hotReloadApplied() through JSImport — but what happens next is identical, and two copies of a pill
// would drift.
//
// The IIFE this used to be wrapped in is gone: a module already has its own scope, and the wrapper
// existed only because the splice pasted this text into the middle of somebody else's function body.

// How long the pill stays visible.
const HOT_RELOAD_PILL_MS = 1200;

let hotReloadPill: HTMLDivElement | null = null;
let hotReloadPillTimer: ReturnType<typeof setTimeout> | null = null;

export function showHotReloadPill(): void {
    // Built lazily on first use, so a production bundle constructs no DOM and injects no CSS for
    // it at all — only the (unreachable) function body ships.
    if (!hotReloadPill) {
        const style = document.createElement("style");
        style.setAttribute("data-rask-managed", "");
        style.textContent =
            ".rask-hot{position:fixed;right:12px;bottom:12px;z-index:2147483647;" +
            "padding:6px 12px;border-radius:999px;pointer-events:none;opacity:0;" +
            "background:rgba(20,20,20,.82);color:#fff;transition:opacity .15s ease;" +
            "font:12px/1.4 system-ui,-apple-system,Segoe UI,sans-serif;}" +
            ".rask-hot[data-show]{opacity:1;}";
        document.head.appendChild(style);

        // DOM APIs rather than innerHTML — keeps the runtime clean under a strict CSP, matching
        // installOverlay(). data-rask-managed keeps rask-morph from trimming both nodes: they
        // are framework-owned siblings of the rendered tree, and without it the next morph would
        // delete the pill mid-animation.
        hotReloadPill = document.createElement("div");
        hotReloadPill.className = "rask-hot";
        hotReloadPill.setAttribute("data-rask-managed", "");
        hotReloadPill.setAttribute("aria-live", "polite");
        hotReloadPill.textContent = "Hot reload applied";
        document.documentElement.appendChild(hotReloadPill);
    }

    // A counter the watch E2E waits on, so it asserts the feature rather than sleeping.
    window.__raskHotReloadCount = (window.__raskHotReloadCount || 0) + 1;

    hotReloadPill.setAttribute("data-show", "");
    if (hotReloadPillTimer !== null) clearTimeout(hotReloadPillTimer);
    hotReloadPillTimer = setTimeout(() => {
        if (hotReloadPill) hotReloadPill.removeAttribute("data-show");
        hotReloadPillTimer = null;
    }, HOT_RELOAD_PILL_MS);
}

// Still published on window: each host's notification path reaches it by name, and so does the
// watch E2E. The export above is what the host entries import; this is the compatibility surface.
window.__raskHotReloadPill = showHotReloadPill;
