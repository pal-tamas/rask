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
    })
};
