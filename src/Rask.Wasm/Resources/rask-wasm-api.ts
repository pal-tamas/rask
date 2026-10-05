// WASM-only framework Web-API helpers, spliced into rask.wasm.js ONLY (by the RASK_WASM_API marker).
// These back APIs that can't work on the Server transport, so they must not ship in the Server
// client (rask.js) — keeping the Core shared rask-api.js / rask-pwa.js to genuinely-shared helpers only.
//
// Push, notifications, the badge and the wake lock are MDN's own, from Rask.Web; the install prompt is kept from boot
// by Rask.Web's patches in the shared runtime (Rask.Core/Resources/rask-web-patches.ts). Only the manifest injector
// (page-side boot behaviour WASM provides) stays here.

// PWA web app manifest (driven by WasmHostBuilder.UseManifest / WebAppManifest). Applied at boot:
// relative URLs are made absolute (against <base href>, so sub-path deploys stay correct), then the
// manifest is injected as a data: URL <link rel="manifest"> plus, when the page declares none, a
// <meta name="theme-color">. These sit beside the shell's own <base>/<link rel=icon> and aren't touched
// by the render head morph.
window.__raskPwa = window.__raskPwa || {
    applyManifest: (json: string) => {
        let m: RaskManifest;
        try {
            m = JSON.parse(json) as RaskManifest;
        } catch {
            return;
        }
        const abs = (u: string) => {
            try {
                return new URL(u, document.baseURI).href;
            } catch {
                return u;
            }
        };
        const absIcons = (icons: { src?: string }[] | undefined) => {
            if (!Array.isArray(icons)) return;
            for (let i = 0; i < icons.length; i++) {
                const src = icons[i] && icons[i].src;
                if (src) icons[i].src = abs(src);
            }
        };
        if (m.start_url) m.start_url = abs(m.start_url);
        if (m.scope) m.scope = abs(m.scope);
        absIcons(m.icons);
        absIcons(m.screenshots);
        if (Array.isArray(m.shortcuts)) {
            for (let i = 0; i < m.shortcuts.length; i++) {
                const s = m.shortcuts[i];
                if (s && s.url) s.url = abs(s.url);
                if (s) absIcons(s.icons);
            }
        }
        if (m.share_target && m.share_target.action) m.share_target.action = abs(m.share_target.action);
        if (Array.isArray(m.file_handlers)) {
            for (let i = 0; i < m.file_handlers.length; i++) {
                const f = m.file_handlers[i];
                if (f && f.action) f.action = abs(f.action);
            }
        }
        let link = document.querySelector<HTMLLinkElement>('link[rel="manifest"]');
        if (!link) {
            link = document.createElement("link");
            link.rel = "manifest";
            document.head.appendChild(link);
        }
        link.href = "data:application/manifest+json," + encodeURIComponent(JSON.stringify(m));
        // A fallback only. A theme-color the PAGE declares is the page's own head and wins: overwriting it
        // here held for exactly one frame, until the next head morph put the page's value back — so the
        // browser's toolbar and tab-bar tint (Safari, Chrome on Android) blinked to the manifest colour and
        // back on every boot. It also clobbered the first tag of a light/dark `media` pair. The manifest
        // still carries theme_color for an installed app's window.
        if (m.theme_color && !document.querySelector('meta[name="theme-color"]')) {
            const meta = document.createElement("meta");
            meta.name = "theme-color";
            meta.content = m.theme_color;
            document.head.appendChild(meta);
        }
    }
};

// __raskInstall / __raskOrientation / __raskMedia / __raskPip live in the shared Rask.Core/Resources/rask-api.js and
// browser/globals.js so they also ship to the Server client — the declarative InstallTrigger /
// ScreenOrientationTrigger / MediaCaptureTrigger / PictureInPictureTrigger drive them inside the click gesture there.

// __raskFullscreen / __raskEyeDropper also moved to Rask.Core/Resources/rask-api.js (same reason — the
// declarative FullscreenTrigger / EyeDropperTrigger drive them on the Server client). The imperative
// imperative fullscreen is Rask.Web's, WASM-only.

// Background Sync + Periodic Background Sync (driven by IBackgroundSync). WASM-only: the registration
// lives on the service worker, and a Server app's SW has no client-side runtime to wake into.
//
// Registration goes through getRegistration(), NOT navigator.serviceWorker.ready — `ready` never settles
// when no service worker is registered, so an app that skipped the SW would hang on every call here
// instead of being told plainly that background sync is unavailable.
//
// The SW handler (Resources/rask-sw.js) postMessages each woken-up tag to its clients. Those arriving
// before C# has subscribed are held rather than dropped: the common case is a sync landing while the page
// is still booting after a spell offline, which is precisely the event the app most wants to see.
window.__raskSync = window.__raskSync || (() => {
    // Events that arrived before C# subscribed; flushed by listen().
    const buffered: { periodic: boolean; tag: string }[] = [];
    let listening = false;

    const toDotNet = (periodic: boolean, tag: string) =>
        window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskBackgroundSync", periodic, tag);

    if (navigator.serviceWorker) {
        navigator.serviceWorker.addEventListener("message", (e) => {
            const d = e.data;
            if (!d || (d.rask !== "sync" && d.rask !== "periodicsync")) {
                return;
            }
            const periodic = d.rask === "periodicsync";
            if (listening) {
                toDotNet(periodic, d.tag);
            } else {
                buffered.push({periodic: periodic, tag: d.tag});
            }
        });
    }

    // undefined when there is no SW at all, so every caller below can answer "unavailable" instead of
    // throwing at a call site that has no way to act on it.
    const reg = () => (navigator.serviceWorker
        ? navigator.serviceWorker.getRegistration().catch(() => undefined)
        : Promise.resolve(undefined));

    const onProto = (name: string) =>
        typeof ServiceWorkerRegistration !== "undefined" && name in ServiceWorkerRegistration.prototype;

    return {
        supported: () => onProto("sync"),
        periodicSupported: () => onProto("periodicSync"),

        // Called by C# on the first subscription; idempotent, and flushes whatever arrived during boot.
        listen: () => {
            listening = true;
            const held = buffered.splice(0, buffered.length);
            for (let i = 0; i < held.length; i++) {
                toDotNet(held[i].periodic, held[i].tag);
            }
        },

        request: (tag: string) => reg().then((r) => {
            if (!r || !r.sync) return false;
            return r.sync.register(tag).then(() => true, () => false);
        }),

        tags: () => reg().then((r) => (r && r.sync ? r.sync.getTags() : [])).catch(() => []),

        // "periodic-background-sync" as PermissionName is not a permission name every browser knows; an unknown name
        // rejects (and in some engines throws outright), so both paths land on "denied".
        periodicPermission: () => {
            if (!navigator.permissions) return Promise.resolve("denied");
            try {
                return navigator.permissions.query({name: "periodic-background-sync" as PermissionName})
                    .then((s) => s.state, () => "denied");
            } catch (e) {
                return Promise.resolve("denied");
            }
        },

        requestPeriodic: (tag: string, minIntervalMs: number) => reg().then((r) => {
            if (!r || !r.periodicSync) return false;
            return r.periodicSync.register(tag, {minInterval: minIntervalMs}).then(() => true, () => false);
        }),

        unregisterPeriodic: (tag: string) => reg()
            .then((r) => (r && r.periodicSync ? r.periodicSync.unregister(tag) : undefined))
            .catch(() => undefined),

        periodicTags: () => reg()
            .then((r) => (r && r.periodicSync ? r.periodicSync.getTags() : []))
            .catch(() => [])
    };
})();
