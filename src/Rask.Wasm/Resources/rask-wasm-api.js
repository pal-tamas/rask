// WASM-only framework Web-API helpers, spliced into rask.wasm.js ONLY (by the RASK_WASM_API marker).
// These back APIs that can't work on the Server transport, so they must not ship in the Server
// client (rask.js) — keeping the Core shared rask-api.js to genuinely-shared helpers only.

// Web Push (driven by IWebPush in Rask.Wasm.Browser). Push needs a Service Worker registration plus
// key (de)serialization that IJSRuntime can't express directly, so it all lives here.
window.__raskPush = window.__raskPush || {
    isSupported: () =>
        ("serviceWorker" in navigator) && ("PushManager" in window) && ("Notification" in window),

    requestPermission: () => Notification.requestPermission(),

    register: (swUrl) => navigator.serviceWorker.register(swUrl).then(() => undefined),

    subscribe: async (vapidPublicKey) => {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: window.__raskPush._urlB64ToBytes(vapidPublicKey)
        });
        return window.__raskPush._serialize(sub);
    },

    getSubscription: async () => {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        return sub ? window.__raskPush._serialize(sub) : null;
    },

    unsubscribe: async () => {
        const reg = await navigator.serviceWorker.ready;
        const sub = await reg.pushManager.getSubscription();
        return sub ? await sub.unsubscribe() : false;
    },

    // Shape a live PushSubscription into the C# PushSubscription record (base64url key bytes).
    _serialize: (sub) => ({
        endpoint: sub.endpoint,
        expirationTime: sub.expirationTime,
        p256dh: window.__raskPush._b64url(sub.getKey("p256dh")),
        auth: window.__raskPush._b64url(sub.getKey("auth"))
    }),

    // NB: no regex literals here — the MSBuild client-JS splice mangles backslashes, so base64url
    // (de)coding uses split/join instead of regex replace patterns.
    _b64url: (buf) => {
        if (!buf) return "";
        const bytes = new Uint8Array(buf);
        let s = "";
        for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
        let out = btoa(s).split("+").join("-").split("/").join("_");
        while (out.length > 0 && out[out.length - 1] === "=") out = out.slice(0, -1);
        return out;
    },

    _urlB64ToBytes: (base64) => {
        const pad = "=".repeat((4 - base64.length % 4) % 4);
        const norm = (base64 + pad).split("-").join("+").split("_").join("/");
        const raw = atob(norm);
        const out = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
        return out;
    }
};

// PWA web app manifest (driven by WasmHostBuilder.UseManifest / WebAppManifest). Applied at boot:
// relative URLs are made absolute (against <base href>, so sub-path deploys stay correct), then the
// manifest is injected as a data: URL <link rel="manifest"> plus a <meta name="theme-color">. These
// sit beside the shell's own <base>/<link rel=icon> and aren't touched by the render head morph.
window.__raskPwa = window.__raskPwa || {
    applyManifest: (json) => {
        let m;
        try {
            m = JSON.parse(json);
        } catch (_) {
            return;
        }
        const abs = (u) => {
            try {
                return new URL(u, document.baseURI).href;
            } catch (_) {
                return u;
            }
        };
        const absIcons = (icons) => {
            if (!Array.isArray(icons)) return;
            for (let i = 0; i < icons.length; i++) {
                if (icons[i] && icons[i].src) icons[i].src = abs(icons[i].src);
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
        let link = document.querySelector('link[rel="manifest"]');
        if (!link) {
            link = document.createElement("link");
            link.rel = "manifest";
            document.head.appendChild(link);
        }
        link.href = "data:application/manifest+json," + encodeURIComponent(JSON.stringify(m));
        if (m.theme_color) {
            let meta = document.querySelector('meta[name="theme-color"]');
            if (!meta) {
                meta = document.createElement("meta");
                meta.name = "theme-color";
                document.head.appendChild(meta);
            }
            meta.content = m.theme_color;
        }
    }
};

// PWA install prompt (driven by IInstallPrompt). The browser fires beforeinstallprompt once when the app
// becomes installable; we preventDefault() and stash the event so C# can replay it from a user gesture
// (showing a custom "Install" button) instead of the browser's default mini-infobar. Listeners are
// attached when this helper is first created at boot, so the event isn't missed.
window.__raskInstall = window.__raskInstall || (() => {
    let deferred = null;
    let installed = false;
    window.addEventListener("beforeinstallprompt", (e) => {
        e.preventDefault();
        deferred = e;
    });
    window.addEventListener("appinstalled", () => {
        installed = true;
        deferred = null;
    });
    return {
        canInstall: () => deferred != null,
        isInstalled: () => installed
            || !!(window.matchMedia && window.matchMedia("(display-mode: standalone)").matches)
            || window.navigator.standalone === true,
        prompt: async () => {
            if (!deferred) {
                return "unavailable";
            }
            deferred.prompt();
            let outcome = "dismissed";
            try {
                const choice = await deferred.userChoice;
                outcome = (choice && choice.outcome === "accepted") ? "accepted" : "dismissed";
            } catch (_) {
                outcome = "dismissed";
            }
            deferred = null;
            return outcome;
        }
    };
})();

// Local notifications (driven by INotifications). `new Notification(...)` is a constructor IJSRuntime
// can't call directly, so showing goes through here. Permission read/request are plain calls in C#.
window.__raskNotify = window.__raskNotify || {
    isSupported: () => "Notification" in window,
    show: (title, options) => {
        new Notification(title, options || {});
    }
};

// App badging (driven by IBadge). setAppBadge() with no argument shows a generic dot, with a number
// shows the count — collapse the C# nullable int to that here. clearAppBadge() removes it.
window.__raskBadge = window.__raskBadge || {
    isSupported: () => "setAppBadge" in navigator,
    set: (count) => (count === null || count === undefined)
        ? navigator.setAppBadge()
        : navigator.setAppBadge(count),
    clear: () => navigator.clearAppBadge()
};

// Screen Wake Lock (driven by IWakeLock). A WakeLockSentinel is a live object IJSRuntime can't return,
// so it's kept here under an integer id. Browsers auto-release the lock when the page is hidden, so we
// re-acquire still-held locks when the page becomes visible again — a C# sentinel stays effective until
// it's disposed (which calls release).
window.__raskWakeLock = window.__raskWakeLock || (() => {
    const held = new Map();
    let nextId = 1;
    let visBound = false;

    const track = (entry) => {
        entry.sentinel.addEventListener("release", () => { entry.released = true; });
    };

    const bindVisibility = () => {
        if (visBound) return;
        visBound = true;
        document.addEventListener("visibilitychange", async () => {
            if (document.visibilityState !== "visible") return;
            for (const entry of held.values()) {
                if (!entry.released) continue;
                try {
                    entry.sentinel = await navigator.wakeLock.request("screen");
                    entry.released = false;
                    track(entry);
                } catch (_) { /* best-effort re-acquire */ }
            }
        });
    };

    return {
        isSupported: () => "wakeLock" in navigator,
        request: async () => {
            bindVisibility();
            const entry = { sentinel: await navigator.wakeLock.request("screen"), released: false };
            track(entry);
            const id = nextId++;
            held.set(id, entry);
            return id;
        },
        release: async (id) => {
            const entry = held.get(id);
            if (!entry) return;
            held.delete(id);
            try {
                await entry.sentinel.release();
            } catch (_) { /* already released (e.g. by the page going hidden) */ }
        }
    };
})();

// Screen Orientation (driven by IScreenOrientation). Reading returns the live screen.orientation as a
// plain { type, angle } object (mapped to the typed OrientationInfo in C#); lock/unlock pass through.
window.__raskOrientation = window.__raskOrientation || {
    isSupported: () => "orientation" in screen,
    get: () => ({ type: screen.orientation.type, angle: screen.orientation.angle }),
    lock: (type) => screen.orientation.lock(type),
    unlock: () => { screen.orientation.unlock(); }
};

// Fullscreen (driven by IFullscreen). requestFullscreen needs transient activation, so this is WASM-only.
// The element arg is resolved from an ElementRef by the JSON reviver; with no element the whole page goes
// fullscreen (document.documentElement). exit is a no-op when nothing is fullscreen.
window.__raskFullscreen = window.__raskFullscreen || {
    isSupported: () => !!document.fullscreenEnabled,
    isActive: () => document.fullscreenElement != null,
    request: (el) => (el || document.documentElement).requestFullscreen(),
    exit: () => document.fullscreenElement ? document.exitFullscreen() : Promise.resolve()
};

// EyeDropper (driven by IEyeDropper). open() needs transient activation, so this is WASM-only. The picker
// rejects with AbortError when the user cancels (Escape) — map that to null rather than surfacing an error.
window.__raskEyeDropper = window.__raskEyeDropper || {
    isSupported: () => "EyeDropper" in window,
    open: () => new EyeDropper().open().then((r) => r.sRGBHex, () => null)
};

// Picture-in-Picture (driven by IPictureInPicture). requestPictureInPicture needs transient activation, so
// this is WASM-only. The element arg is resolved from an ElementRef by the JSON reviver; exit is a no-op
// when no miniplayer is open.
window.__raskPip = window.__raskPip || {
    isSupported: () => !!document.pictureInPictureEnabled,
    isActive: () => document.pictureInPictureElement != null,
    request: (el) => el ? el.requestPictureInPicture() : Promise.reject(new Error("no video element")),
    exit: () => document.pictureInPictureElement ? document.exitPictureInPicture() : Promise.resolve()
};

// Idle Detection (driven by IIdleDetector). Permission needs transient activation and the detector needs
// the live document, so this is WASM-only. Each watch holds a live IdleDetector + AbortController under the
// C#-minted id and pushes each change back via window.DotNet.invokeMethodAsync (static [JSInvokable]
// IdleDetectorInterop.Changed in Rask.Wasm — the WASM DotNet dispatcher resolves any assembly name).
window.__raskIdle = window.__raskIdle || (() => {
    const detectors = new Map();
    return {
        isSupported: () => "IdleDetector" in window,
        requestPermission: () =>
            window.IdleDetector ? IdleDetector.requestPermission().catch(() => "denied") : Promise.resolve("denied"),
        watch: async (id, thresholdSeconds) => {
            const controller = new AbortController();
            const detector = new IdleDetector();
            detector.addEventListener("change", () => {
                window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskIdleChanged", id, {
                    userIdle: detector.userState === "idle",
                    screenLocked: detector.screenState === "locked"
                });
            });
            // The spec enforces a 60-second floor; clamp here so a smaller value doesn't reject.
            await detector.start({threshold: Math.max(60, thresholdSeconds) * 1000, signal: controller.signal});
            detectors.set(id, controller);
        },
        unwatch: (id) => {
            const controller = detectors.get(id);
            if (!controller) {
                return;
            }
            detectors.delete(id);
            controller.abort();
        }
    };
})();
