// Dev-only "hot reload applied" indicator — one implementation, spliced into all three transports
// (Server rask.js, WASM rask.wasm.js, Native rask.native.js) at their hot-reload splice marker.
//
// Deliberately does not spell that marker out: this file's text is substituted *for* it, so a literal
// copy here would survive into every built artifact and read as a marker the splice had missed.
//
// It lives here rather than in each dialect because the transports differ in how the *notification*
// arrives — Server pushes a {"type":"hotReload"} frame over the socket, WASM calls the exported
// hotReloadApplied() through JSImport, Native evaluates a call over the WebView bridge — but what
// happens next is identical, and three copies of a pill would drift.
//
// Wrapped in its own IIFE so its locals cannot collide with the enclosing dialect (the Server client
// is one big IIFE with its own `const`s; WASM and Native splice at module top level). The single
// export is window.__raskHotReloadPill, which is how each transport reaches it.
(function () {
    "use strict";

    // How long the pill stays visible.
    const HOT_RELOAD_PILL_MS = 1200;

    let hotReloadPill = null;
    let hotReloadPillTimer = null;

    function showHotReloadPill() {
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
            // installOverlay(). data-rask-managed keeps rask-morph.js from trimming both nodes: they
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
        hotReloadPillTimer = setTimeout(function () {
            if (hotReloadPill) hotReloadPill.removeAttribute("data-show");
            hotReloadPillTimer = null;
        }, HOT_RELOAD_PILL_MS);
    }

    window.__raskHotReloadPill = showHotReloadPill;
})();
