// Shared framework Web-API / interop helpers, spliced into both client runtimes
// (Server rask.js and WASM rask.wasm.js) by the RASK_API build marker. Single source of
// truth so the two transports never drift. Each helper is assigned to a `window.__rask*`
// namespace so a dotted IJSRuntime identifier (e.g. "__raskApi.geolocation") resolves to it.

// Element-ref helpers, invoked from C# via ElementRef.FocusAsync/Blur/ScrollIntoView.
// The JSON reviver resolves an ElementRef arg to the live DOM element, so each receives it.
window.__raskEl = window.__raskEl || {
    focus: (el) => {
        if (el) el.focus();
    },
    blur: (el) => {
        if (el) el.blur();
    },
    scrollIntoView: (el, opts) => {
        if (el) el.scrollIntoView(opts || {behavior: "smooth", block: "nearest"});
    }
};

// Web-API helpers for callback-shaped browser APIs that IJSRuntime can't await directly.
// Property reads (navigator.onLine, localStorage.length) and Promise-returning methods
// (clipboard.readText) need no helper — the invoke dispatcher returns the value / awaits the
// Promise on its own. getCurrentPosition is callback-based, so wrap it in a Promise here.
window.__raskApi = window.__raskApi || {
    geolocation: (enableHighAccuracy, timeoutMs, maximumAgeMs) => new Promise((resolve, reject) => {
        if (!navigator.geolocation) {
            reject(new Error("Geolocation is not supported in this browser."));
            return;
        }
        const opts = {enableHighAccuracy: !!enableHighAccuracy, maximumAge: maximumAgeMs || 0};
        if (timeoutMs != null) opts.timeout = timeoutMs;
        navigator.geolocation.getCurrentPosition(
            (pos) => {
                const c = pos.coords;
                resolve({
                    latitude: c.latitude,
                    longitude: c.longitude,
                    accuracy: c.accuracy,
                    altitude: c.altitude,
                    altitudeAccuracy: c.altitudeAccuracy,
                    heading: c.heading,
                    speed: c.speed,
                    timestampMs: pos.timestamp
                });
            },
            (err) => reject(new Error((err && err.message) || ("Geolocation error " + (err && err.code)))),
            opts);
    }),

    // Permissions API: query resolves to a live PermissionStatus object — return just its .state
    // string so it serializes back to C# cleanly.
    permissionState: (name) => navigator.permissions.query({name: name}).then((s) => s.state),

    // Cookies via document.cookie. Reads parse the cookie string; writes/deletes build the
    // assignment string (a bare `document.cookie = …` is a property write IJSRuntime can't express).
    cookieGet: (name) => {
        const prefix = encodeURIComponent(name) + "=";
        const parts = document.cookie ? document.cookie.split("; ") : [];
        for (let i = 0; i < parts.length; i++) {
            if (parts[i].indexOf(prefix) === 0) {
                return decodeURIComponent(parts[i].slice(prefix.length));
            }
        }
        return null;
    },
    cookieAll: () => {
        const out = {};
        const parts = document.cookie ? document.cookie.split("; ") : [];
        for (let i = 0; i < parts.length; i++) {
            const eq = parts[i].indexOf("=");
            if (eq > 0) {
                out[decodeURIComponent(parts[i].slice(0, eq))] = decodeURIComponent(parts[i].slice(eq + 1));
            }
        }
        return out;
    },
    cookieSet: (name, value, maxAge, expires, path, domain, sameSite, secure) => {
        let s = encodeURIComponent(name) + "=" + encodeURIComponent(value);
        if (maxAge != null) s += "; max-age=" + maxAge;
        if (expires) s += "; expires=" + expires;
        if (path) s += "; path=" + path;
        if (domain) s += "; domain=" + domain;
        if (sameSite) s += "; samesite=" + sameSite;
        if (secure) s += "; secure";
        document.cookie = s;
    },
    cookieDelete: (name, path) => {
        document.cookie = encodeURIComponent(name) + "=; max-age=0" + (path ? "; path=" + path : "");
    },

    // matchMedia (driven by IMediaQuery): evaluate a CSS media query and return just the boolean
    // .matches from the live MediaQueryList.
    matchMedia: (query) => window.matchMedia(query).matches,

    // Storage estimate (driven by IStorageEstimator): navigator.storage.estimate() resolves to a live
    // object — return a plain { quota, usage } snapshot (mapped to StorageEstimate in C#), or null when
    // unsupported.
    storageSupported: () => !!(navigator.storage && navigator.storage.estimate),
    storageEstimate: async () => {
        if (!(navigator.storage && navigator.storage.estimate)) {
            return null;
        }
        const e = await navigator.storage.estimate();
        return {quota: e.quota || 0, usage: e.usage || 0};
    },

    // Screen / display info (driven by IScreenInfo): a snapshot of window.screen plus devicePixelRatio,
    // mapped to the ScreenInfo record in C#.
    screen: () => ({
        width: screen.width,
        height: screen.height,
        availWidth: screen.availWidth,
        availHeight: screen.availHeight,
        colorDepth: screen.colorDepth,
        pixelRatio: window.devicePixelRatio
    }),

    // Speech synthesis (driven by ISpeechSynthesis): new SpeechSynthesisUtterance(...) is a constructor
    // IJSRuntime can't call, so build it here and speak. Support/cancel are plain checks.
    speechSupported: () => "speechSynthesis" in window,
    speak: (text, options) => {
        if (!("speechSynthesis" in window)) {
            return;
        }
        const u = new SpeechSynthesisUtterance(text);
        if (options) {
            if (options.lang) u.lang = options.lang;
            if (typeof options.rate === "number") u.rate = options.rate;
            if (typeof options.pitch === "number") u.pitch = options.pitch;
            if (typeof options.volume === "number") u.volume = options.volume;
        }
        window.speechSynthesis.speak(u);
    },
    cancelSpeech: () => {
        if ("speechSynthesis" in window) {
            window.speechSynthesis.cancel();
        }
    },

    // Network Information: navigator.connection is a live, vendor-prefixed object. Return a plain
    // snapshot (mapped to NetworkStatus in C#), or null when unsupported (Firefox/Safari).
    networkSupported: () =>
        !!(navigator.connection || navigator.mozConnection || navigator.webkitConnection),
    network: () => {
        const c = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
        if (!c) {
            return null;
        }
        return {
            effectiveType: c.effectiveType || null,
            downlink: typeof c.downlink === "number" ? c.downlink : 0,
            rtt: typeof c.rtt === "number" ? c.rtt : 0,
            saveData: !!c.saveData
        };
    }
};
