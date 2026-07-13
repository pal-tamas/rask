// Rask NATIVE client runtime — ES module, loaded by index.native.html inside a platform WebView.
//
// This is the third Rask client "dialect" alongside rask.js (Server, WebSocket) and rask.wasm.js
// (browser WASM, JSImport). It speaks the SAME frame contract as both — the shared diff codec
// (rask-dom.js), full-HTML morph (rask-morph.js), interop helpers (rask-api.js), extended DOM events
// (rask-events.js) and PWA helpers (rask-pwa.js) are spliced in verbatim at the markers below, so the
// DOM-side behaviour is identical across transports. Only the TRANSPORT differs:
//
//   • send(payload)         → posts JSON to the native host over window.__raskSend (WKScriptMessageHandler
//                             on iOS, a [JavascriptInterface] on Android). The host's NativeLiveSession
//                             turns it into a handler/navigate dispatch.
//   • window.__raskNative.applyRender(json)   ← the host calls this (via EvaluateJavaScript) with each
//                             rendered frame; it drives applyDiff / morph exactly like the WASM client.
//   • window.__raskNative.beginInvokeJS / endDotNetInvoke  ← the host calls these for IJSRuntime interop;
//                             results are posted back through send({type:'jsResult'|'dotNetInvoke'}).
//   • On load we post {type:'ready'} so the host fires its first render only once the client is live.
//
// NOTE (PoC parity): the primary click/input/change/submit handlers below are ported from rask.wasm.js.
// Full parity for the remaining transport-side DOM helpers — rAF input/scroll coalescing, keyboard/drag/
// file events, scoped-CSS FOUC gating and Rask.* scoped-JS invoke gating — is a tracked follow-up: those
// blocks are large and identical to rask.wasm.js and should be lifted into a shared module rather than
// re-copied. See docs/native.md.

let root = null;

// ----- Shared framework interop helpers (__raskEl, __raskApi) — Rask.Core/Resources/rask-api.js -----
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

    // Visual viewport (driven by IVisualViewport): window.visualViewport is a live object — return a plain
    // snapshot (mapped to VisualViewport in C#), or null when unsupported.
    visualViewportSupported: () => !!window.visualViewport,
    visualViewport: () => {
        const v = window.visualViewport;
        if (!v) {
            return null;
        }
        return {
            width: v.width,
            height: v.height,
            offsetLeft: v.offsetLeft,
            offsetTop: v.offsetTop,
            pageLeft: v.pageLeft,
            pageTop: v.pageTop,
            scale: v.scale
        };
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

// IndexedDB key/value store (driven by IIndexedDb / IKeyValueStore). Each named store is its own IndexedDB
// database with a single object store; the open connection is cached. Every op is wrapped in a
// transaction-scoped Promise so IJSRuntime can await a plain value.
window.__raskIdb = window.__raskIdb || (() => {
    const STORE = "kv";
    const dbs = new Map();

    const open = (name) => {
        if (dbs.has(name)) {
            return dbs.get(name);
        }
        const p = new Promise((resolve, reject) => {
            const req = indexedDB.open(name, 1);
            req.onupgradeneeded = () => { req.result.createObjectStore(STORE); };
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        dbs.set(name, p);
        return p;
    };

    // Run fn(objectStore) in a transaction; resolve with the request's result once the transaction commits.
    const run = (name, mode, fn) => open(name).then((db) => new Promise((resolve, reject) => {
        const t = db.transaction(STORE, mode);
        const req = fn(t.objectStore(STORE));
        t.oncomplete = () => resolve(req && req.result !== undefined ? req.result : null);
        t.onerror = () => reject(t.error);
        t.onabort = () => reject(t.error);
    }));

    return {
        isSupported: () => "indexedDB" in window,
        open: (name) => open(name).then(() => undefined),
        set: (name, key, value) => run(name, "readwrite", (s) => s.put(value, key)).then(() => undefined),
        get: (name, key) => run(name, "readonly", (s) => s.get(key)).then((v) => (v === undefined ? null : v)),
        delete: (name, key) => run(name, "readwrite", (s) => s.delete(key)).then(() => undefined),
        keys: (name) => run(name, "readonly", (s) => s.getAllKeys()),
        clear: (name) => run(name, "readwrite", (s) => s.clear()).then(() => undefined)
    };
})();

// Performance (driven by IPerformance): performance.now() through a helper (stable `this`), and the
// navigation timing entry plucked into a plain object (mapped to NavigationTiming in C#), or null.
window.__raskPerf = window.__raskPerf || {
    now: () => performance.now(),
    navigation: () => {
        const entries = performance.getEntriesByType ? performance.getEntriesByType("navigation") : [];
        const e = entries && entries.length ? entries[0] : null;
        if (!e) {
            return null;
        }
        return {
            timeToFirstByteMs: e.responseStart,
            domInteractiveMs: e.domInteractive,
            domContentLoadedMs: e.domContentLoadedEventEnd,
            loadMs: e.loadEventEnd,
            durationMs: e.duration
        };
    }
};

// Web Crypto (driven by ICrypto). getRandomValues fills a typed array and subtle.digest returns an
// ArrayBuffer; return plain bytes / a lowercase hex string so IJSRuntime can marshal them. No regex
// literals (the client-JS splice mangles backslashes), so hex uses a lookup, not String.prototype.padStart
// on a radix string.
window.__raskCrypto = window.__raskCrypto || {
    randomUuid: () => crypto.randomUUID(),
    randomBytes: (length) => Array.from(crypto.getRandomValues(new Uint8Array(length))),
    digestHex: async (algorithm, text) => {
        const data = new TextEncoder().encode(text);
        const buf = await crypto.subtle.digest(algorithm, data);
        const bytes = new Uint8Array(buf);
        let hex = "";
        for (let i = 0; i < bytes.length; i++) {
            const h = bytes[i].toString(16);
            hex += (h.length === 1 ? "0" : "") + h;
        }
        return hex;
    }
};

// Geolocation watch (driven by IGeolocation.WatchAsync). navigator.geolocation.watchPosition pushes each
// fix; forward it to C# via the shared window.DotNet.invokeMethodAsync shim (static [JSInvokable]
// GeolocationWatchInterop.Fix in Rask.Core). The fix object matches the GeolocationPosition record (same
// shape as __raskApi.geolocation). Errors are ignored so the watch keeps trying.
window.__raskGeoWatch = window.__raskGeoWatch || (() => {
    const watches = new Map();
    return {
        watch: (id, enableHighAccuracy, timeoutMs, maximumAgeMs) => {
            if (!navigator.geolocation) {
                return;
            }
            const opts = {enableHighAccuracy: !!enableHighAccuracy, maximumAge: maximumAgeMs || 0};
            if (timeoutMs != null) {
                opts.timeout = timeoutMs;
            }
            const watchId = navigator.geolocation.watchPosition(
                (pos) => {
                    const c = pos.coords;
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskGeolocationFix", id, {
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
                () => { /* error: keep watching, surface nothing */ },
                opts);
            watches.set(id, watchId);
        },
        clear: (id) => {
            const watchId = watches.get(id);
            if (watchId == null) {
                return;
            }
            watches.delete(id);
            navigator.geolocation.clearWatch(watchId);
        }
    };
})();

// Resize Observer (driven by IResizeObserver). Each observation is a live ResizeObserver held here under
// the C#-minted id; each size change is pushed back to C# via the shared window.DotNet.invokeMethodAsync
// shim (static [JSInvokable] ResizeInterop.Changed in Rask.Core). The element is resolved from an
// ElementRef by the JSON reviver.
window.__raskResize = window.__raskResize || (() => {
    const observers = new Map();
    return {
        observe: (id, element) => {
            if (!element) {
                return;
            }
            const ro = new ResizeObserver((entries) => {
                for (let i = 0; i < entries.length; i++) {
                    const r = entries[i].contentRect;
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskResizeChanged", id, {
                        width: r.width,
                        height: r.height
                    });
                }
            });
            ro.observe(element);
            observers.set(id, ro);
        },
        unobserve: (id) => {
            const ro = observers.get(id);
            if (!ro) {
                return;
            }
            observers.delete(id);
            ro.disconnect();
        }
    };
})();

// Intersection Observer (driven by IIntersectionObserver). Each observation is a live
// IntersectionObserver held here under the C#-minted id; each change is pushed back to C# via the shared
// window.DotNet.invokeMethodAsync shim (static [JSInvokable] IntersectionInterop.Changed in Rask.Core).
// The element is resolved from an ElementRef by the JSON reviver.
window.__raskIntersect = window.__raskIntersect || (() => {
    const observers = new Map();
    return {
        observe: (id, element, thresholds, rootMargin) => {
            if (!element) {
                return;
            }
            const opts = {threshold: (thresholds && thresholds.length) ? thresholds : 0};
            if (rootMargin) {
                opts.rootMargin = rootMargin;
            }
            const io = new IntersectionObserver((entries) => {
                for (let i = 0; i < entries.length; i++) {
                    const e = entries[i];
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskIntersectionChanged", id, {
                        isIntersecting: e.isIntersecting,
                        ratio: e.intersectionRatio
                    });
                }
            }, opts);
            io.observe(element);
            observers.set(id, io);
        },
        unobserve: (id) => {
            const io = observers.get(id);
            if (!io) {
                return;
            }
            observers.delete(id);
            io.disconnect();
        }
    };
})();

// Device Orientation (driven by IDeviceOrientation). Each watch adds a window "deviceorientation"
// listener under the C#-minted id; each reading is pushed back via the shared window.DotNet.invokeMethodAsync
// shim (static [JSInvokable] DeviceOrientationInterop.Reading in Rask.Core).
window.__raskDeviceOrientation = window.__raskDeviceOrientation || (() => {
    const listeners = new Map();
    return {
        isSupported: () => "DeviceOrientationEvent" in window,
        requestPermission: () => {
            const evt = window.DeviceOrientationEvent;
            if (!evt) {
                return Promise.resolve("denied");
            }
            if (typeof evt.requestPermission === "function") {
                return evt.requestPermission().catch(() => "denied");
            }
            return Promise.resolve("granted");
        },
        watch: (id) => {
            // Sensors fire ~60 Hz; throttle to ~10 Hz before crossing the interop boundary so a moving
            // device doesn't flood the Server WebSocket / re-render loop.
            let last = 0;
            const handler = (e) => {
                const now = Date.now();
                if (now - last < 100) {
                    return;
                }
                last = now;
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskDeviceOrientation", id, {
                    alpha: e.alpha,
                    beta: e.beta,
                    gamma: e.gamma,
                    absolute: !!e.absolute
                });
            };
            window.addEventListener("deviceorientation", handler);
            listeners.set(id, handler);
        },
        clear: (id) => {
            const handler = listeners.get(id);
            if (!handler) {
                return;
            }
            listeners.delete(id);
            window.removeEventListener("deviceorientation", handler);
        }
    };
})();

// Device Motion (driven by IDeviceMotion). Each watch adds a window "devicemotion" listener under the
// C#-minted id; each reading is pushed back via the shared window.DotNet.invokeMethodAsync shim (static
// [JSInvokable] DeviceMotionInterop.Reading in Rask.Core).
window.__raskDeviceMotion = window.__raskDeviceMotion || (() => {
    const listeners = new Map();
    return {
        isSupported: () => "DeviceMotionEvent" in window,
        requestPermission: () => {
            const evt = window.DeviceMotionEvent;
            if (!evt) {
                return Promise.resolve("denied");
            }
            if (typeof evt.requestPermission === "function") {
                return evt.requestPermission().catch(() => "denied");
            }
            return Promise.resolve("granted");
        },
        watch: (id) => {
            // Sensors fire ~60 Hz; throttle to ~10 Hz before crossing the interop boundary so a moving
            // device doesn't flood the Server WebSocket / re-render loop.
            let last = 0;
            const handler = (e) => {
                const now = Date.now();
                if (now - last < 100) {
                    return;
                }
                last = now;
                const a = e.acceleration || {};
                const r = e.rotationRate || {};
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskDeviceMotion", id, {
                    accelerationX: a.x,
                    accelerationY: a.y,
                    accelerationZ: a.z,
                    rotationAlpha: r.alpha,
                    rotationBeta: r.beta,
                    rotationGamma: r.gamma,
                    interval: e.interval
                });
            };
            window.addEventListener("devicemotion", handler);
            listeners.set(id, handler);
        },
        clear: (id) => {
            const handler = listeners.get(id);
            if (!handler) {
                return;
            }
            listeners.delete(id);
            window.removeEventListener("devicemotion", handler);
        }
    };
})();

// Media Session (driven by IMediaSession). Metadata/playback state are one-shot setters; each action
// handler is wired to a C#-minted id and pushed back via the shared window.DotNet.invokeMethodAsync shim
// (static [JSInvokable] MediaSessionInterop.Invoke in Rask.Core), so one wiring serves both transports.
window.__raskMediaSession = window.__raskMediaSession || (() => {
    const actions = new Map();   // id -> action
    const owners = new Map();    // action -> id of the registration the browser currently holds
    return {
        isSupported: () => "mediaSession" in navigator,
        setMetadata: (m) => {
            navigator.mediaSession.metadata = new MediaMetadata({
                title: m.title || "",
                artist: m.artist || "",
                album: m.album || "",
                artwork: m.artwork || []
            });
        },
        setPlaybackState: (state) => {
            navigator.mediaSession.playbackState = state;
        },
        setActionHandler: (id, action) => {
            navigator.mediaSession.setActionHandler(action, () => {
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskMediaSessionAction", id);
            });
            actions.set(id, action);
            owners.set(action, id);
        },
        removeActionHandler: (id) => {
            const action = actions.get(id);
            if (action === undefined) {
                return;
            }
            actions.delete(id);
            // Only clear the browser handler if this id still owns the action — a newer registration for
            // the same action must not be clobbered when an older disposable is disposed.
            if (owners.get(action) === id) {
                owners.delete(action);
                navigator.mediaSession.setActionHandler(action, null);
            }
        },
        clear: () => {
            navigator.mediaSession.metadata = null;
            navigator.mediaSession.playbackState = "none";
        }
    };
})();

// Mutation Observer (driven by IMutationObserver). Each observation is a live MutationObserver held here
// under the C#-minted id; each record is pushed back to C# via the shared window.DotNet.invokeMethodAsync
// shim (static [JSInvokable] MutationInterop.Changed in Rask.Core). The element is resolved from an
// ElementRef by the JSON reviver.
window.__raskMutation = window.__raskMutation || (() => {
    const observers = new Map();
    return {
        observe: (id, element, childList, attributes, characterData, subtree, attributeFilter) => {
            if (!element) {
                return;
            }
            const opts = {
                childList: !!childList,
                attributes: !!attributes,
                characterData: !!characterData,
                subtree: !!subtree
            };
            if (attributeFilter && attributeFilter.length) {
                // An attributeFilter requires attributes:true, so honour the "implies Attributes" contract
                // instead of letting MutationObserver.observe throw.
                opts.attributeFilter = attributeFilter;
                opts.attributes = true;
            }
            const mo = new MutationObserver((records) => {
                for (let i = 0; i < records.length; i++) {
                    const r = records[i];
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskMutationChanged", id, {
                        type: r.type,
                        addedCount: r.addedNodes ? r.addedNodes.length : 0,
                        removedCount: r.removedNodes ? r.removedNodes.length : 0,
                        attributeName: r.attributeName
                    });
                }
            });
            mo.observe(element, opts);
            observers.set(id, mo);
        },
        unobserve: (id) => {
            const mo = observers.get(id);
            if (!mo) {
                return;
            }
            observers.delete(id);
            mo.disconnect();
        }
    };
})();

// Broadcast Channel (driven by IBroadcastChannel). Each connection is a live BroadcastChannel held here
// under the C#-minted integer id; an incoming message is pushed back to C# via the shared
// window.DotNet.invokeMethodAsync shim (static [JSInvokable] BroadcastInterop.Receive in Rask.Core),
// which works on both transports. A channel does not receive its own posts.
window.__raskBroadcast = window.__raskBroadcast || (() => {
    const channels = new Map();
    return {
        open: (id, name) => {
            const ch = new BroadcastChannel(name);
            ch.onmessage = (e) => {
                const data = typeof e.data === "string" ? e.data : JSON.stringify(e.data);
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskBroadcastReceive", id, data);
            };
            channels.set(id, ch);
        },
        post: (id, message) => {
            const ch = channels.get(id);
            if (ch) {
                ch.postMessage(message);
            }
        },
        close: (id) => {
            const ch = channels.get(id);
            if (!ch) {
                return;
            }
            channels.delete(id);
            ch.close();
        }
    };
})();

// Gamepad (driven by IGamepad). The Gamepad API has no input event, so each watch runs a
// requestAnimationFrame poll of navigator.getGamepads() under the C#-minted id and pushes a reading back
// via the shared window.DotNet.invokeMethodAsync shim (static [JSInvokable] GamepadInterop.Reading in
// Rask.Core) ONLY when a pad's state changes — throttled to ~12 Hz so a held stick doesn't flood the
// transport. rAF is paused by the browser while the tab is hidden, which also pauses the poll.
window.__raskGamepad = window.__raskGamepad || (() => {
    const watchers = new Map();
    return {
        isSupported: () => "getGamepads" in navigator,
        watch: (id) => {
            let last = 0;
            let raf = 0;
            const prev = new Map(); // pad index -> last serialized snapshot
            const tick = () => {
                const now = Date.now();
                if (now - last >= 80) {
                    last = now;
                    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
                    const live = new Set();
                    for (let i = 0; i < pads.length; i++) {
                        const p = pads[i];
                        if (!p) {
                            continue;
                        }
                        live.add(p.index);
                        const axes = Array.prototype.map.call(p.axes, (a) => Math.round(a * 1000) / 1000);
                        const buttons = Array.prototype.map.call(p.buttons, (b) => b.value);
                        const snap = axes.join(",") + "|" + buttons.join(",") + "|" + p.connected;
                        if (prev.get(p.index) !== snap) {
                            prev.set(p.index, snap);
                            window.DotNet.invokeMethodAsync("Rask.Core", "RaskGamepadReading", id, {
                                index: p.index,
                                id: p.id,
                                connected: p.connected,
                                axes: axes,
                                buttons: buttons
                            });
                        }
                    }
                    // Emit a final disconnect reading for pads that vanished since the last poll.
                    prev.forEach((_, index) => {
                        if (!live.has(index)) {
                            prev.delete(index);
                            window.DotNet.invokeMethodAsync("Rask.Core", "RaskGamepadReading", id, {
                                index: index,
                                id: "",
                                connected: false,
                                axes: [],
                                buttons: []
                            });
                        }
                    });
                }
                raf = requestAnimationFrame(tick);
            };
            raf = requestAnimationFrame(tick);
            watchers.set(id, () => cancelAnimationFrame(raf));
        },
        unwatch: (id) => {
            const stop = watchers.get(id);
            if (!stop) {
                return;
            }
            watchers.delete(id);
            stop();
        }
    };
})();

// File System Access (driven by IFileSystemAccess). The opaque FileSystemFileHandle / DirectoryHandle
// objects can't cross the interop boundary, so each is held here under a C#-minted id and operated on by
// id. Pickers reject with AbortError when the user cancels — map that to null / [] rather than an error.
// Bytes ride the boundary base64-encoded.
window.__raskFs = window.__raskFs || (() => {
    const handles = new Map();
    let nextId = 0;
    const put = (handle) => {
        const id = ++nextId;
        handles.set(id, handle);
        return {id: id, name: handle.name};
    };
    const types = (opts) => {
        if (!opts || !opts.accept) {
            return undefined;
        }
        return [{description: opts.description || "", accept: opts.accept}];
    };
    const isAbort = (e) => e && e.name === "AbortError";
    return {
        isSupported: () => "showOpenFilePicker" in window,
        openFile: async (opts) => {
            try {
                const picked = await window.showOpenFilePicker({multiple: false, types: types(opts)});
                return put(picked[0]);
            } catch (e) {
                if (isAbort(e)) {
                    return null;
                }
                throw e;
            }
        },
        openFiles: async (opts) => {
            try {
                const picked = await window.showOpenFilePicker({multiple: true, types: types(opts)});
                return picked.map(put);
            } catch (e) {
                if (isAbort(e)) {
                    return [];
                }
                throw e;
            }
        },
        saveFile: async (opts) => {
            try {
                const handle = await window.showSaveFilePicker({
                    suggestedName: (opts && opts.suggestedName) || undefined,
                    types: types(opts)
                });
                return put(handle);
            } catch (e) {
                if (isAbort(e)) {
                    return null;
                }
                throw e;
            }
        },
        openDirectory: async () => {
            try {
                return put(await window.showDirectoryPicker());
            } catch (e) {
                if (isAbort(e)) {
                    return null;
                }
                throw e;
            }
        },
        readText: async (id) => {
            const file = await handles.get(id).getFile();
            return await file.text();
        },
        readBytes: async (id) => {
            const file = await handles.get(id).getFile();
            const bytes = new Uint8Array(await file.arrayBuffer());
            let binary = "";
            for (let i = 0; i < bytes.length; i++) {
                binary += String.fromCharCode(bytes[i]);
            }
            return btoa(binary);
        },
        writeText: async (id, text) => {
            const writable = await handles.get(id).createWritable();
            await writable.write(text);
            await writable.close();
        },
        writeBytes: async (id, base64) => {
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }
            const writable = await handles.get(id).createWritable();
            await writable.write(bytes);
            await writable.close();
        },
        list: async (id) => {
            const names = [];
            for await (const name of handles.get(id).keys()) {
                names.push(name);
            }
            return names;
        },
        getFile: async (id, name, create) => {
            const handle = await handles.get(id).getFileHandle(name, {create: !!create});
            return put(handle);
        },
        release: (id) => {
            handles.delete(id);
        }
    };
})();

// Web Authentication / passkeys (driven by IWebAuthn). The credential shapes are ArrayBuffer-heavy, so this
// helper base64url-(de)codes the binary fields at the seam — challenge / user.id / credential ids go in as
// base64url, and rawId / clientDataJSON / attestationObject / authenticatorData / signature / userHandle come
// back as base64url, ready to POST to a relying-party backend. A user cancellation / timeout
// (NotAllowedError / AbortError) resolves to null rather than throwing.
window.__raskWebAuthn = window.__raskWebAuthn || (() => {
    // base64url <-> ArrayBuffer. Uses split/join rather than regex literals: the framework's JS minifier
    // mis-parses regex literals (a bare /.../ reads as division), which would break the spliced bundle.
    const b64urlToBuf = (s) => {
        let pad = "";
        if (s.length % 4 !== 0) {
            for (let i = 0; i < 4 - (s.length % 4); i++) {
                pad += "=";
            }
        }
        const bin = atob(s.split("-").join("+").split("_").join("/") + pad);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) {
            bytes[i] = bin.charCodeAt(i);
        }
        return bytes.buffer;
    };
    const bufToB64url = (buf) => {
        const bytes = new Uint8Array(buf);
        let bin = "";
        for (let i = 0; i < bytes.length; i++) {
            bin += String.fromCharCode(bytes[i]);
        }
        // Strip "=" padding (base64 only uses it as trailing padding), then make it URL-safe.
        return btoa(bin).split("=").join("").split("+").join("-").split("/").join("_");
    };
    const descriptors = (list) => (list || []).map((d) => ({
        type: d.type || "public-key",
        id: b64urlToBuf(d.id),
        transports: d.transports || undefined
    }));
    const isCancel = (e) => e && (e.name === "NotAllowedError" || e.name === "AbortError");
    return {
        isSupported: () => !!(window.PublicKeyCredential && navigator.credentials),
        platformAuthenticatorAvailable: () =>
            (window.PublicKeyCredential && PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable)
                ? PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable()
                : Promise.resolve(false),
        create: async (o) => {
            const publicKey = {
                challenge: b64urlToBuf(o.challenge),
                rp: o.rp,
                user: {id: b64urlToBuf(o.user.id), name: o.user.name, displayName: o.user.displayName},
                pubKeyCredParams: (o.pubKeyCredParams && o.pubKeyCredParams.length)
                    ? o.pubKeyCredParams
                    : [{type: "public-key", alg: -7}, {type: "public-key", alg: -257}],
                timeout: o.timeoutMs || undefined,
                attestation: o.attestation || undefined,
                authenticatorSelection: o.authenticatorSelection || undefined,
                excludeCredentials: o.excludeCredentials ? descriptors(o.excludeCredentials) : undefined
            };
            let cred;
            try {
                cred = await navigator.credentials.create({publicKey: publicKey});
            } catch (e) {
                if (isCancel(e)) {
                    return null;
                }
                throw e;
            }
            if (!cred) {
                return null;
            }
            return {
                id: cred.id,
                rawId: bufToB64url(cred.rawId),
                type: cred.type,
                clientDataJson: bufToB64url(cred.response.clientDataJSON),
                attestationObject: bufToB64url(cred.response.attestationObject),
                transports: cred.response.getTransports ? cred.response.getTransports() : null
            };
        },
        get: async (o) => {
            const publicKey = {
                challenge: b64urlToBuf(o.challenge),
                timeout: o.timeoutMs || undefined,
                rpId: o.rpId || undefined,
                allowCredentials: o.allowCredentials ? descriptors(o.allowCredentials) : undefined,
                userVerification: o.userVerification || undefined
            };
            let cred;
            try {
                cred = await navigator.credentials.get({publicKey: publicKey});
            } catch (e) {
                if (isCancel(e)) {
                    return null;
                }
                throw e;
            }
            if (!cred) {
                return null;
            }
            return {
                id: cred.id,
                rawId: bufToB64url(cred.rawId),
                type: cred.type,
                clientDataJson: bufToB64url(cred.response.clientDataJSON),
                authenticatorData: bufToB64url(cred.response.authenticatorData),
                signature: bufToB64url(cred.response.signature),
                userHandle: cred.response.userHandle ? bufToB64url(cred.response.userHandle) : null
            };
        }
    };
})();


// ----- Transport-agnostic PWA helpers (__raskPush/__raskNotify/__raskBadge/__raskWakeLock) -----
// Transport-agnostic PWA framework helpers, spliced into BOTH the Server client (rask.js) and the WASM
// client (rask.wasm.js) at their shared PWA splice marker. These back the PWA browser APIs that work on
// either transport — IWebPush (subscribe), INotifications, IBadge, IWakeLock (all in Rask.Core.Browser).
// The WASM-only helpers that need transient activation / a static SW instance (the manifest injector and
// install-prompt capture) and the device APIs stay in Rask.Wasm's rask-wasm-api.js.
//
// NB: no regex literals here — the MSBuild client-JS splice mangles backslashes, so base64url
// (de)coding uses split/join instead of regex replace patterns.

// Web Push (driven by IWebPush). Push needs a Service Worker registration plus key (de)serialization
// that IJSRuntime can't express directly, so it all lives here. The SW URL is resolved by C#
// (IWebPush.RegisterServiceWorkerAsync defaults to {PathBase}/rask-sw.js — the WASM boot asset or the
// Server AddRaskPwa endpoint).
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


// ----- The diff codec: applyDiff(ops, names) + applyFrameInvokes(reply, dispatchOne) — rask-dom.js -----
// Shared diff-codec interpreter consumed by both rask.js (Server) and
// rask.wasm.js (WASM). Concatenated into each runtime at build time — see the
// MSBuild "_RaskBuildClientJs" target in Rask.Server.csproj and
// "_RaskSpliceClientJs" in Rask.Wasm.csproj (they splice this file at the
// RASK_DOM marker).
//
// Why concat instead of import / network split (same rationale as rask-morph.js):
//  - rask.js is a classic <script> served from /rask/rask.js (no ES-module hook).
//  - rask.wasm.js is loaded by JSHost.ImportAsync as an ES module.
// Concat sidesteps the loader mismatch and keeps the single-file delivery model.
//
// Modern JS is fine here (current-browser targets), with the same two splice
// constraints as rask-morph.js: the top-level helpers stay hoisted `function`
// declarations — applyDiff calls reviveScript() and raskShouldSuppressValue()
// (both defined in rask-morph.js, spliced into the same scope) regardless of
// splice order — and no `export` / `import` (this island is spliced inside the
// Server's classic-script IIFE, where module syntax is illegal).

// ----- Diff codec interpreter --------------------------------------------
// Applies ops produced by C#-side FrameDiffer.Diff to the live DOM. Each op
// names its target via a Path = sequence of childNodes indices from `document`.
// The Path is computed by the diff walker counting only DOM-relevant frames
// (Element, Text, Raw, Doctype) and excluding Attribute frames, which matches
// the browser's `Node.childNodes` collection semantics for the rendered HTML.
//
// Each op is a positional array; the kind at op[0] selects which trailing slots
// are present (mirrors LivePayload.BuildPayloadUtf8Diff exactly):
//   1 SetAttribute     [k, path, name|idx, value]
//   2 RemoveAttribute  [k, path, name|idx]
//   3 UpdateText       [k, path, value]
//   4 InsertSubtree    [k, path, html, domCount]
//   5 RemoveSubtree    [k, path, domCount]
//   6 MoveSubtree      [k, path, sourceSlot]
//   7 PermutationBatch [k, parentPath, moves]
//   8 MorphSubtree     [k, path, innerHtml]
//
// Names for SetAttribute/RemoveAttribute may arrive as either a string (inline) or
// a number that indexes into the optional payload-level "names" array — the server
// interns names that appear 2+ times in the same payload to drop the duplicate
// string bytes. resolveName() handles either form.
// Comment nodes shift childNodes indices relative to the server's frame walk.
// Filter to DOM-relevant nodes only (Element=1, Text=3, Doctype=10) so paths
// match what FrameDiffer counts.
const _relevantNodeTypes = {1: 1, 3: 1, 10: 1};

function relevantChild(parent, index) {
    if (!parent || !parent.childNodes) return null;
    let seen = 0;
    for (const n of parent.childNodes) {
        if (_relevantNodeTypes[n.nodeType]) {
            if (seen === index) return n;
            seen++;
        }
    }
    return null;
}

// Like relevantChild but counts as if `skip` were already gone — the post-detach
// coordinate the keyed differ uses for move targets. Lets us resolve the anchor
// WITHOUT detaching the moving node, so the move can run as a single relocation.
function relevantChildSkipping(parent, index, skip) {
    if (!parent || !parent.childNodes) return null;
    let seen = 0;
    for (const n of parent.childNodes) {
        if (n === skip) continue;
        if (_relevantNodeTypes[n.nodeType]) {
            if (seen === index) return n;
            seen++;
        }
    }
    return null;
}

// Relocate `node` before `ref` under `parent`. Prefer the Atomic Move API
// (moveBefore, Chromium 133+): it moves the node WITHOUT disconnecting it, so a
// focused descendant keeps its focus, selection, and caret across a keyed reorder.
// removeChild+insertBefore — and even a bare insertBefore — disconnect the node
// and blur it, which silently broke the "survivors keep their DOM state" contract.
// Fall back to insertBefore where moveBefore is unavailable or rejects the move.
function moveChildBefore(parent, node, ref) {
    if (parent.moveBefore) {
        try {
            parent.moveBefore(node, ref);
            return;
        } catch (e) {
            // Not connected / cross-document — fall through to insertBefore.
        }
    }
    parent.insertBefore(node, ref);
}

function resolvePath(path) {
    let node = document;
    for (const slot of path) {
        node = relevantChild(node, slot);
        if (!node) return null;
    }
    return node;
}

// Mirror selected attribute writes onto the matching IDL property. After user
// interaction, an input's `value` attribute is the *default*, not the current
// state — setAttribute does not reach the live value. Same for `checked` on
// checkboxes/radios and `selected` on options. Only sync when the element
// supports the property so we don't silently no-op on unrelated tags.
//
// Active-element guard: when the diff would overwrite the value of the focused
// input, the server's view is racing with the user's keystrokes (the server
// rendered with a value computed before the latest key landed). Skipping the
// sync on the focused element keeps the user's in-flight typing intact; the
// next keystroke updates server state and any subsequent render reconciles.
function syncFormProperty(el, name, value, isPresent) {
    // `isPresent` tells us whether the attribute is set or being removed —
    // separate from the value because the HTML attributes `checked`/`selected`
    // are presence-based: `<input checked>`, `<input checked="">`, and
    // `<input checked="checked">` all mean checked. RemoveAttribute → unchecked.
    if (!el) return;
    const tag = el.tagName;
    if (!tag) return;
    if (name === "value" && (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT")) {
        if (document.activeElement === el) return;
        if (raskShouldSuppressValue(el, value)) return;
        el.value = value;
    } else if (name === "checked" && tag === "INPUT") {
        if (raskShouldSuppressChecked(el, !!isPresent)) return;
        el.checked = !!isPresent;
    } else if (name === "selected" && tag === "OPTION") {
        el.selected = !!isPresent;
    }
}

function applyDiff(ops, names) {
    function resolveName(raw) {
        // Server interns names that repeat 2+ times in the same payload — those
        // arrive as integer indices into the "names" array. Strings pass through.
        if (typeof raw === "number" && names) return names[raw];
        return raw;
    }

    for (const op of ops) {
        const k = op[0];
        const path = op[1] || [];
        switch (k) {
            case 1: { // SetAttribute [k, path, name|idx, value]
                const el = resolvePath(path);
                if (el && el.setAttribute) {
                    const name1 = resolveName(op[2]);
                    const rawVal = op[3];
                    const newVal = rawVal == null ? "" : rawVal;
                    el.setAttribute(name1, newVal);
                    // After a form-control has been interacted with, the value
                    // attribute is desynchronised from the .value/.checked property
                    // (the attribute is the *default*, not the current state). Sync
                    // the IDL property too so user-visible state matches the diff.
                    syncFormProperty(el, name1, newVal, true);
                }
                break;
            }
            case 2: { // RemoveAttribute [k, path, name|idx]
                const el2 = resolvePath(path);
                if (el2 && el2.removeAttribute) {
                    const name2 = resolveName(op[2]);
                    el2.removeAttribute(name2);
                    syncFormProperty(el2, name2, "", false);
                }
                break;
            }
            case 3: { // UpdateText [k, path, value]
                const textNode = resolvePath(path);
                if (textNode) {
                    // UpdateText only ever targets a Text node now: the diff codec emits it
                    // exclusively for changed Text frames (HTML-encoded content), so
                    // .textContent is the correct knob. A changed Raw frame is NOT an
                    // UpdateText — its verbatim markup parses into a variable run of DOM
                    // nodes that textContent would escape and could not fully replace, so the
                    // codec ships it as a Remove+Insert that routes to the full-HTML morph.
                    const txtVal = op[2];
                    textNode.textContent = txtVal == null ? "" : txtVal;
                }
                break;
            }
            case 4: { // InsertSubtree [k, path, html, domCount]
                const insertHtml = op[2];
                if (typeof insertHtml !== "string") {
                    console.warn("[Rask] InsertSubtree without payload — server " +
                        "must include HTML fragment. Falling back to full reload.");
                    location.reload();
                    return;
                }
                const parentPath = path.slice(0, path.length - 1);
                const slot = path[path.length - 1];
                const parent = resolvePath(parentPath);
                if (!parent) break;
                const template = document.createElement("template");
                template.innerHTML = insertHtml;
                // Scripts parsed via innerHTML carry the "already started" flag and will
                // NOT execute when inserted into the live document. Rebuild them via
                // reviveScript so a scoped <script src="/_rask/a/{hash}.js"> (or a user
                // Head <script>) delivered through a keyed InsertSubtree diff actually
                // runs — otherwise its window.Rask.{Type}/global never appears. Mirrors
                // the full-HTML morph path, which already revives inserted scripts.
                for (const oldScript of template.content.querySelectorAll("script")) {
                    oldScript.parentNode.replaceChild(reviveScript(oldScript), oldScript);
                }
                const refNode = parent.childNodes[slot] || null;
                while (template.content.firstChild) {
                    parent.insertBefore(template.content.firstChild, refNode);
                }
                break;
            }
            case 5: { // RemoveSubtree [k, path, domCount]
                const rmParentPath = path.slice(0, path.length - 1);
                const rmSlot = path[path.length - 1];
                const rmParent = resolvePath(rmParentPath);
                if (!rmParent) break;
                const removeCount = op[2] || 1;
                for (let r = 0; r < removeCount; r++) {
                    const victim = rmParent.childNodes[rmSlot];
                    if (!victim) break;
                    rmParent.removeChild(victim);
                }
                break;
            }
            case 6: { // MoveSubtree [k, path, sourceSlot]
                // Path encodes parent + destination slot; op[2] is the source slot.
                // The destination slot is in the server's post-detach coordinate
                // (the live DOM with the moved node removed), so resolve the anchor
                // by SKIPPING the moving node rather than detaching it — then relocate
                // with moveChildBefore so a focused descendant keeps focus/selection.
                const mvParentPath = path.slice(0, path.length - 1);
                const mvDst = path[path.length - 1];
                const mvParent = resolvePath(mvParentPath);
                if (!mvParent) break;
                const mvSrcRaw = op[2];
                const mvSrc = mvSrcRaw == null ? 0 : mvSrcRaw;
                const mvNode = relevantChild(mvParent, mvSrc);
                if (!mvNode) break;
                const mvRef = relevantChildSkipping(mvParent, mvDst, mvNode);
                moveChildBefore(mvParent, mvNode, mvRef);
                break;
            }
            case 7: { // PermutationBatch [k, parentPath, moves] — moves = [dst0,src0,dst1,src1,…]
                // path IS the parent (no trailing slot to split off). Replay each (dst,src)
                // pair in array order: the server computed every pair against the live DOM
                // as mutated by the preceding pairs, so order is load-bearing — never reorder.
                // Each dst is a post-detach slot, so resolve the anchor by skipping the moving
                // node and relocate with moveChildBefore (preserves focus across the reorder).
                const pbParent = resolvePath(path);
                if (!pbParent) break;
                const pbMoves = op[2] || [];
                for (let m = 0; m + 1 < pbMoves.length; m += 2) {
                    const pbDst = pbMoves[m];
                    const pbSrc = pbMoves[m + 1];
                    const pbNode = relevantChild(pbParent, pbSrc);
                    if (!pbNode) continue;
                    const pbRef = relevantChildSkipping(pbParent, pbDst, pbNode);
                    moveChildBefore(pbParent, pbNode, pbRef);
                }
                break;
            }
            case 8: { // MorphSubtree [k, path, innerHtml]
                // The Raw-tainted fallback, scoped: reconcile the CHILDREN of the element at `path`
                // against fresh inner HTML via the same morph() the full-document path uses — but
                // localised to this one subtree the server could still address by a clean path. A Raw's
                // markup expands into an unknown DOM-node count, so the server can't emit reliable
                // positional child ops here; a morph reparses it correctly and preserves keyed / focus /
                // IDL state on everything it doesn't need to touch (incl. the rest of the document).
                const msEl = resolvePath(path);
                if (!msEl) break;
                // Shallow-clone the ACTUAL parent (not a generic <template>) so innerHTML parses in the
                // element's own context — correct for <table>/<select>/<tr>/… children. The clone carries
                // msEl's current attributes (already reconciled by any SetAttribute ops applied before
                // this one), so morph sees them matching and only touches the children.
                const model = msEl.cloneNode(false);
                model.innerHTML = op[2] == null ? "" : op[2];
                morph(msEl, model);
                break;
            }
            default:
                // Unknown op kind — newer server, older client. Bail to full reload
                // so the user isn't stranded on a stale tree.
                console.warn("[Rask] Unknown diff op kind: " + k);
                location.reload();
                return;
        }
    }
}

// ----- Frame jsInvokes dispatch ------------------------------------------
// The IJSRuntime calls a render frame carried (reply.jsInvokes) run HERE — after applyDiff/morph
// has patched the DOM — so each acts on the committed DOM (e.g. focus a <dialog> that just gained
// its `open` attribute). Both clients call this right after applying the body; only the per-invoke
// executor differs per host (Server posts the result over the WS; WASM returns it through the
// endInvokeJSResult JSExport), so the caller passes dispatchOne. Shared so the loop isn't copied.
function applyFrameInvokes(reply, dispatchOne) {
    const invokes = reply && reply.jsInvokes;
    if (!invokes || typeof invokes.length !== "number") return;
    for (const inv of invokes) {
        if (inv && typeof inv.identifier === "string") dispatchOne(inv);
    }
}

// ----- Focus trap (data-rask-focus-trap) ---------------------------------
// Generic accessible-overlay focus management, driven declaratively so any overlay (Rask.Bootstrap's
// BsModal, or your own) opts in with a single attribute. For as long as an element carrying
// data-rask-focus-trap is in the DOM:
//   * focus moves into it on appear (the [autofocus] element, else the element itself), remembering
//     what had focus so it can be restored on close;
//   * Tab / Shift+Tab cycle within it — focus can't escape to the inert page behind;
//   * Escape closes it by clicking its own / a descendant [data-rask-dismiss] control (a real Rask
//     click handler), so there is no per-keystroke server round-trip;
//   * focus returns to the previously-focused element when the trap leaves the DOM.
// A single document MutationObserver tracks appear/disappear (works with the diff morph that adds and
// removes the overlay); keydown is handled at capture so it fires wherever focus currently sits.
(function installRaskFocusTrap() {
    if (typeof document === "undefined" || typeof MutationObserver === "undefined"
        || window.__raskFocusTrap) {
        return;
    }
    window.__raskFocusTrap = true;

    // No escaped quotes in this selector on purpose: the WASM client-JS splice mangles a backslash in a
    // spliced body, so the negative-tabindex exclusion is done in focusables() via el.tabIndex instead of
    // a [tabindex="-1"] attribute selector (which also correctly excludes tabindex=-1 on any element).
    const FOCUSABLE = "a[href],area[href],button:not([disabled]),"
        + "input:not([disabled]):not([type=hidden]),select:not([disabled]),textarea:not([disabled]),"
        + "[tabindex],[contenteditable=true]";

    let currentTrap = null;
    let restoreTo = null;

    // The topmost trap in the DOM (last in document order) wins when several are open (stacked modals).
    function activeTrap() {
        const traps = document.querySelectorAll("[data-rask-focus-trap]");
        return traps.length ? traps[traps.length - 1] : null;
    }

    function focusables(trap) {
        return Array.prototype.filter.call(
            trap.querySelectorAll(FOCUSABLE),
            (el) => el.tabIndex >= 0
                && (el.offsetWidth > 0 || el.offsetHeight > 0 || el === document.activeElement));
    }

    function enter(trap) {
        // Focus the [autofocus] element if the author marked one, else the trap itself (it carries
        // tabindex=-1 so screen readers announce the dialog). Deferred to rAF so the just-morphed-in
        // element is laid out before we move focus.
        const target = trap.querySelector("[autofocus]") || trap;
        requestAnimationFrame(function () {
            try {
                target.focus();
            } catch (e) {
                // element may have been removed again already
            }
        });
    }

    function restore() {
        const el = restoreTo;
        restoreTo = null;
        if (el && typeof el.focus === "function") {
            try {
                el.focus();
            } catch (e) {
                // previously-focused element is gone
            }
        }
    }

    function sync() {
        const trap = activeTrap();
        if (trap === currentTrap) {
            return;
        }

        if (!currentTrap && trap) {
            restoreTo = document.activeElement; // first trap opened over the page
        }

        currentTrap = trap;
        if (trap) {
            enter(trap);
        } else {
            restore(); // last trap closed
        }
    }

    document.addEventListener("keydown", function (e) {
        const trap = currentTrap;
        if (!trap) {
            return;
        }

        if (e.key === "Escape") {
            const dismiss = trap.hasAttribute("data-rask-dismiss")
                ? trap
                : trap.querySelector("[data-rask-dismiss]");
            if (dismiss) {
                e.preventDefault();
                dismiss.click();
            }
            return;
        }

        if (e.key !== "Tab") {
            return;
        }

        const items = focusables(trap);
        if (!items.length) {
            e.preventDefault(); // nothing to move to — keep focus off the page behind
            return;
        }

        const first = items[0];
        const last = items[items.length - 1];
        const active = document.activeElement;
        if (e.shiftKey && (active === first || active === trap || !trap.contains(active))) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && (active === last || !trap.contains(active))) {
            e.preventDefault();
            first.focus();
        }
    }, true);

    // Only re-scan when a mutation actually adds or removes a trap (or a subtree containing one), so the
    // observer stays cheap on the frequent unrelated morphs.
    function touchesTrap(nodes) {
        for (let i = 0; i < nodes.length; i++) {
            const n = nodes[i];
            if (n.nodeType === 1
                && (n.matches("[data-rask-focus-trap]") || n.querySelector("[data-rask-focus-trap]"))) {
                return true;
            }
        }
        return false;
    }

    const observer = new MutationObserver(function (records) {
        for (let i = 0; i < records.length; i++) {
            if (touchesTrap(records[i].addedNodes) || touchesTrap(records[i].removedNodes)) {
                sync();
                return;
            }
        }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
    sync(); // a trap already present at load
})();

// ----- Overflow-escaping popover (data-rask-popover) ---------------------
// The Popper-less .dropdown-menu components (BsDatePicker/BsTimePicker/BsDateTimePicker, BsDropdown,
// BsMultiSelect) render their menu as position:absolute inside a .dropdown wrapper, so any ancestor
// with overflow:hidden/auto (a card, a scroll region) clips it — the menu opens but is cut off. This
// helper re-anchors an open menu with position:fixed + viewport-computed coordinates, which resolves
// against the viewport and so escapes every overflow-clipping ancestor. A component opts in by marking
// its .dropdown wrapper with data-rask-popover and its trigger with data-rask-anchor; while the
// wrapper's .dropdown-menu carries .show the menu is placed below the trigger (flipping above when it
// doesn't fit), clamped into the viewport, right-aligned when data-rask-popover-align="end". A single
// document MutationObserver watches the .show class toggle (the menus persist in the DOM), and
// capture-phase scroll + resize keep the menu pinned to the trigger.
//
// Caveat: position:fixed only escapes overflow when NO ancestor establishes a fixed containing block
// (a non-none transform / filter / perspective / backdrop-filter / will-change of those, or contain:
// paint/layout/strict/content). Inside such an ancestor the menu is clamped to that box instead of the
// viewport — a browser rule, not a Rask bug. Selectors here carry no escaped quotes/backslashes (the
// WASM client-JS splice mangles them, as noted on the focus trap above).
(function installRaskPopover() {
    if (typeof document === "undefined" || typeof MutationObserver === "undefined"
        || window.__raskPopover) {
        return;
    }
    window.__raskPopover = true;

    const GAP = 2;      // px between the trigger and the menu
    const MARGIN = 8;   // min px kept between the menu and every viewport edge
    const Z = 1000;     // above the components' fixed click-outside backdrop (z-index 999)

    // Every opted-in wrapper that currently has an open (.show) menu, paired with that menu.
    function openMenus() {
        const pairs = [];
        const wraps = document.querySelectorAll("[data-rask-popover]");
        for (let i = 0; i < wraps.length; i++) {
            const menu = wraps[i].querySelector(".dropdown-menu.show");
            if (menu) {
                pairs.push({ wrap: wraps[i], menu: menu });
            }
        }
        return pairs;
    }

    function anchorOf(wrap) {
        return wrap.querySelector("[data-rask-anchor]")
            || wrap.querySelector(".dropdown-toggle")
            || wrap.firstElementChild;
    }

    function place(wrap, menu) {
        const anchor = anchorOf(wrap);
        if (!anchor) {
            return;
        }
        // Clear our own height cap before measuring so the menu's natural size drives placement — else
        // each reposition would feed the previous frame's cap back in. (Width is pinned and stable below,
        // so it needs no such reset.)
        menu.style.maxHeight = "";
        // Measure BEFORE switching to fixed: a menu sized with w-100 (BsMultiSelect) still reports the
        // trigger width here, but would stretch to the viewport once position:fixed — so pin that width.
        const a = anchor.getBoundingClientRect();
        const m = menu.getBoundingClientRect();
        const natH = menu.scrollHeight; // full content height, unaffected by the maxHeight we apply below
        const vw = document.documentElement.clientWidth;
        const vh = document.documentElement.clientHeight;
        const alignEnd = wrap.getAttribute("data-rask-popover-align") === "end";

        // Vertical: below by default; flip above only when it doesn't fit below and there is more room up.
        const roomBelow = vh - a.bottom - GAP - MARGIN;
        const roomAbove = a.top - GAP - MARGIN;
        let top = (natH <= roomBelow || roomBelow >= roomAbove)
            ? a.bottom + GAP
            : a.top - GAP - natH;
        if (top < MARGIN) {
            top = MARGIN;
        }

        // Horizontal: align to the trigger's start (or end), then clamp into the viewport.
        let left = alignEnd ? (a.right - m.width) : a.left;
        if (left + m.width > vw - MARGIN) {
            left = vw - MARGIN - m.width;
        }
        if (left < MARGIN) {
            left = MARGIN;
        }

        menu.style.position = "fixed";
        menu.style.margin = "0";
        menu.style.zIndex = "" + Z;
        // Pin with !important priority: a w-100 menu (BsSelect/BsMultiSelect) carries Bootstrap's
        // .w-100 { width: 100% !important }, which a plain inline width can't beat — so once the menu is
        // position:fixed the 100% would resolve against the viewport (the initial containing block) and
        // stretch it viewport-wide. An inline !important outranks the class !important, pinning the width
        // we measured while it was still position:absolute (== the trigger width). reset() clears it.
        menu.style.setProperty("width", m.width + "px", "important");
        menu.style.left = left + "px";
        menu.style.top = top + "px";
        // Cap the height to the space between the menu top and the viewport bottom and scroll internally,
        // so a menu taller than the viewport (a long list, a calendar on a short window) stays fully
        // reachable instead of overflowing off-screen — a fixed element can't be revealed by page scroll
        // the way the old position:absolute menu could.
        menu.style.maxHeight = (vh - top - MARGIN) + "px";
        menu.style.overflowY = "auto";
        // A fixed menu no longer clips together with its trigger, so hide it while the trigger is scrolled
        // entirely out of the viewport rather than leaving it floating detached over unrelated content.
        menu.style.visibility =
            (a.bottom <= 0 || a.top >= vh || a.right <= 0 || a.left >= vw) ? "hidden" : "";
    }

    // Return a closed menu to its normal in-flow (position:absolute) rendering.
    function reset(menu) {
        menu.style.position = "";
        menu.style.margin = "";
        menu.style.zIndex = "";
        menu.style.width = "";
        menu.style.left = "";
        menu.style.top = "";
        menu.style.maxHeight = "";
        menu.style.overflowY = "";
        menu.style.visibility = "";
    }

    // Re-place every open menu; returns how many were open so callers can track whether any remain.
    function reposition() {
        const pairs = openMenus();
        for (let i = 0; i < pairs.length; i++) {
            place(pairs[i].wrap, pairs[i].menu);
        }
        return pairs.length;
    }

    // True while at least one popover menu is open. Kept so the observer can cheaply skip idle morphs
    // (no open menu, nothing popover-related changed) without a document query.
    let hasOpen = false;

    // Coalesce the high-frequency scroll/resize path to one run per animation frame so a burst doesn't
    // thrash layout (each place() reads geometry then writes styles).
    let scheduled = false;
    function scheduleReposition() {
        if (scheduled) {
            return;
        }
        scheduled = true;
        requestAnimationFrame(function () {
            scheduled = false;
            hasOpen = reposition() > 0;
        });
    }

    // Scroll doesn't bubble, but a capture-phase listener on window still receives it from any ancestor
    // scroller, so the menu tracks the trigger when a card / scroll region (not just the page) scrolls.
    window.addEventListener("scroll", scheduleReposition, true);
    window.addEventListener("resize", scheduleReposition);

    // Does a mutation batch touch a popover (a menu's class toggled, or a subtree add/remove containing
    // one)? Used only to detect the open transition when nothing was open before.
    function touchesPopover(nodes) {
        for (let i = 0; i < nodes.length; i++) {
            const n = nodes[i];
            if (n.nodeType === 1
                && (n.matches("[data-rask-popover],.dropdown-menu")
                    || n.querySelector("[data-rask-popover],.dropdown-menu"))) {
                return true;
            }
        }
        return false;
    }

    // On the open transition, move focus into the menu's [autofocus] element (the searchable BsSelect's
    // filter input) so the user can type immediately — Rask only auto-focuses [autofocus] inside a
    // data-rask-focus-trap (modal), which a plain dropdown is not. Idempotent via __raskOpen so a
    // re-render that rewrites the still-open menu's class doesn't steal focus back on every keystroke.
    // Deferred to rAF so the just-morphed-in field is laid out before we focus it. A menu with no
    // [autofocus] (the date/time pickers) keeps focus on its editable trigger — no change.
    function onOpen(wrap, menu) {
        if (menu.__raskOpen) {
            return;
        }
        menu.__raskOpen = true;
        const af = menu.querySelector("[autofocus]");
        if (!af) {
            return;
        }
        menu.__raskReturn = anchorOf(wrap) || null; // where to send focus back on close
        requestAnimationFrame(function () {
            try {
                af.focus();
            } catch (e) {
                // field removed again already
            }
        });
    }

    // On close, return focus to the trigger (like a native <select>) so keyboard flow continues from the
    // box — but only when we had moved focus into the filter, and only if focus is still loose (on <body>
    // because the filter was removed, or anywhere inside the wrapper), never yanking focus the user moved.
    function onClose(wrap, menu) {
        if (!menu.__raskOpen) {
            return;
        }
        menu.__raskOpen = false;
        const ret = menu.__raskReturn;
        menu.__raskReturn = null;
        if (ret) {
            const ae = document.activeElement;
            if (ae === document.body || (wrap.contains && wrap.contains(ae))) {
                try {
                    ret.focus();
                } catch (e) {
                    // trigger gone (component unmounted)
                }
            }
        }
    }

    // The live-diff morph reconciles each element's attributes back to the rendered output, and the
    // rendered menu carries no inline style — so ANY re-render of a component with an open menu strips the
    // fixed positioning we wrote (an unrelated style-attribute write the class-only observer never sees).
    // So while a menu is open we must re-place after every morph batch, not only when the menu node itself
    // changed — hence `hasOpen` in the gate. When nothing is open and nothing popover-related changed, the
    // gate skips the document query entirely, so idle live-diff churn stays free. Runs synchronously (in
    // the mutation microtask) so a just-opened menu is fixed before anything can read it as absolute.
    const observer = new MutationObserver(function (records) {
        let touched = false;
        for (let i = 0; i < records.length; i++) {
            const r = records[i];
            if (r.type === "attributes") {
                const t = r.target;
                if (t.nodeType === 1 && t.classList && t.classList.contains("dropdown-menu")) {
                    touched = true;
                    const pop = t.closest("[data-rask-popover]");
                    if (pop) {
                        if (t.classList.contains("show")) {
                            onOpen(pop, t); // just opened — focus its [autofocus] filter
                        } else {
                            reset(t);       // just closed — drop the fixed inline styles
                            onClose(pop, t);
                        }
                    }
                }
            } else if (touchesPopover(r.addedNodes) || touchesPopover(r.removedNodes)) {
                touched = true;
            }
        }
        if (touched || hasOpen) {
            hasOpen = reposition() > 0;
        }
    });
    observer.observe(document.documentElement,
        { subtree: true, childList: true, attributes: true, attributeFilter: ["class"] });

    // While a popover is open, suppress the NATIVE side-effects of the combobox navigation/commit keys so
    // they act only inside the dropdown — most importantly Enter, which in the filter <input> would
    // otherwise fire the surrounding <form>'s implicit submit (validating the whole form) instead of just
    // picking the highlighted option. We only preventDefault, never stopPropagation: the C# keydown
    // handler is dispatched on the document bubble phase (rask-events.js), so the event must still reach
    // it to select / navigate / close. Printable keys, Space and Left/Right are left alone so typing into
    // the filter keeps working. Capture-phase so we run before the browser commits the default action.
    const CONTAIN = ["Enter", "Escape", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown"];
    document.addEventListener("keydown", function (e) {
        if (!hasOpen || CONTAIN.indexOf(e.key) < 0) {
            return;
        }
        const wrap = (e.target && e.target.closest) ? e.target.closest("[data-rask-popover]") : null;
        if (wrap && wrap.querySelector(".dropdown-menu.show")) {
            e.preventDefault();
        }
    }, true);

    hasOpen = reposition() > 0; // a menu already open at load
})();

// ----- Recovery affordance (data-rask-reload) ----------------------------
// A click on any element carrying data-rask-reload reloads the page. Used by the default error page so a
// user stranded on an uncaught fault has an in-app way back without hunting for the browser's reload.
// Delegated + CSP-clean (no inline handler); a no-op if the runtime never loaded (the browser's own
// reload remains the ultimate fallback).
(function installRaskReload() {
    if (typeof document === "undefined" || typeof document.addEventListener !== "function"
        || typeof window === "undefined" || window.__raskReload) {
        return;
    }
    window.__raskReload = true;
    document.addEventListener("click", function (e) {
        const t = e.target;
        if (t && t.closest && t.closest("[data-rask-reload]")) {
            e.preventDefault();
            location.reload();
        }
    });
})();


// ----- The full-HTML morph: morph(target, fresh) + reviveScript — rask-morph.js -----
// Shared client-side morph algorithm consumed by both rask.js (Server) and
// rask.wasm.js (WASM). Concatenated into each runtime at build time — see the
// MSBuild "_RaskBuildClientJs" target in Rask.Server.csproj and Rask.Wasm.csproj.
//
// Why concat instead of import / network split:
//  - rask.js is a classic <script> served from /rask/rask.js (no ES-module hook).
//  - rask.wasm.js is loaded by JSHost.ImportAsync as an ES module.
// Concat sidesteps the loader mismatch and keeps the single-file delivery model.
//
// Modern JS is fine here — both runtimes target current browsers (the codec uses
// moveBefore / crypto.randomUUID). Two splice constraints, not a dialect one:
//  - The top-level helpers stay hoisted `function` declarations, NOT `const fn =
//    () => …`: applyDiff (rask-dom.js) calls reviveScript() / raskShouldSuppressValue()
//    here, and the two files concatenate into one scope in EITHER order, so the
//    cross-references must resolve regardless of splice ordering (hoisting). Locals,
//    callbacks, and literals inside them use modern syntax freely.
//  - No `export` / `import`: this island is spliced inside the Server's classic-script
//    IIFE, where module syntax is illegal.

// Scripts produced by DOMParser have their "already started" flag set, so the
// browser silently skips them when morph() appends them into the live document.
// Rebuild script nodes via createElement so they actually execute, propagate
// every attribute (type=module, defer, integrity, nonce, crossorigin, …), and
// fire raskAfterMorph again once external scripts finish loading — inline
// scripts run synchronously on insertion and may early-return if they depend
// on a not-yet-loaded global like window.hljs.
function reviveScript(node) {
    if (!node || node.nodeType !== 1 || node.tagName !== "SCRIPT") return node;
    const s = document.createElement("script");
    for (const a of node.attributes) s.setAttribute(a.name, a.value);
    if (s.src) {
        s.async = false;
        s.addEventListener("load", () => {
            if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
        }, {once: true});
    }
    s.text = node.textContent;
    return s;
}

// Wrappers around the underlying DOM mutation primitives. Scoped-JS hooks are
// not auto-fired by morph — C# components drive invocations explicitly via
// `IJSRuntime.InvokeVoidAsync("Rask.{TypeName}.{method}", ...args)` from a
// lifecycle hook (typically OnRenderedAsync). Calls land in RaskJSRuntime
// (Server) or WasmJSRuntime (WASM), are dispatched against the freshly-morphed
// DOM, and Rask.*-prefixed identifiers are gated by a pending queue so calls
// that race the scoped-JS bundle drain after it loads. If a component needs
// teardown on element removal, install a MutationObserver inside the hook or
// expose an explicit "removed" method and call it from OnUnmount.
function _raskInsertBefore(parent, dst, anchor) {
    parent.insertBefore(dst, anchor);
}

// Relocate an already-attached child before `anchor`. Prefer the Atomic Move API
// (moveBefore, Chromium 133+): it moves the node WITHOUT disconnecting it, so a
// focused descendant keeps focus, selection, and caret across a keyed reorder. A
// plain insertBefore of a connected node still disconnects it briefly and blurs it.
function _raskMoveBefore(parent, node, anchor) {
    if (parent.moveBefore) {
        try {
            parent.moveBefore(node, anchor);
            return;
        } catch (e) {
            // Not connected / cross-document — fall through to insertBefore.
        }
    }
    parent.insertBefore(node, anchor);
}

function _raskAppendChild(parent, dst) {
    parent.appendChild(dst);
}

function _raskRemoveChild(parent, src) {
    parent.removeChild(src);
}

function _raskReplaceChild(parent, dst, src) {
    parent.replaceChild(dst, src);
}

// Lagging-render value guard. When a user commits a change on a change-only input
// (date / number / select), a re-render the server computed BEFORE that change
// reached it can land afterwards and clobber the user's value. The focus guard in
// morph() only protects the *focused* element, but a change commits on blur, so by
// the time the lagging frame arrives focus has already moved on.
//
// On the change dispatch the runtime records the input's PRE-EDIT value (its last
// server-rendered `value` attribute) — exactly what such a lagging frame carries.
// A subsequent server value is suppressed only while it equals that recorded value;
// any other value is the authoritative response to the user's change — the echo of
// the new value OR a server correction/normalisation (e.g. clearing a non-nullable
// int snaps the model to 0) — so it applies and releases the guard. Recording the
// pre-edit value (not the user's new value) is what lets a correction through:
// suppress-if-equal-to-stale, not suppress-unless-equal-to-mine.
//
// Keyed by element identity — morph patches inputs in place, so identity survives
// across re-renders. Backed by a window global so the helper is reachable from both
// the spliced morph (here) and the host runtime's event / diff code (rask.js,
// rask.wasm.js), regardless of splice ordering.
function _raskPendingValues() {
    return window.__raskPendingValues || (window.__raskPendingValues = new WeakMap());
}

function raskNotePendingValue(el, supersededValue) {
    if (el) _raskPendingValues().set(el, supersededValue);
}

function raskShouldSuppressValue(el, incoming) {
    const map = _raskPendingValues();
    if (!el || !map.has(el)) return false;
    if (map.get(el) === incoming) return true;   // lagging frame carrying the stale value
    map.delete(el);                               // authoritative response — release the guard
    return false;
}

// The `.checked` analogue of the value guard above. A native radio/checkbox click flips the
// `.checked` PROPERTY but leaves the `checked` ATTRIBUTE untouched, so the change dispatch records
// the pre-click attribute state (raskNotePendingChecked) — exactly as the value guard records the
// pre-edit `value` attribute. A lagging frame the server computed BEFORE the click reached it still
// carries that stale checked, so it's suppressed until an authoritative frame (the echo of the new
// state OR a server correction) arrives with a different value and releases the guard. For a radio
// the dispatch records the whole same-name group, so a stale frame can't re-check the previously
// selected radio (which would natively uncheck the new one). Kept a hoisted `function` so the
// spliced rask-dom.js can call it regardless of splice ordering — same rationale as the value guard.
function _raskPendingChecked() {
    return window.__raskPendingChecked || (window.__raskPendingChecked = new WeakMap());
}

function raskNotePendingChecked(el, supersededChecked) {
    if (el) _raskPendingChecked().set(el, !!supersededChecked);
}

function raskShouldSuppressChecked(el, incoming) {
    const map = _raskPendingChecked();
    if (!el || !map.has(el)) return false;
    if (map.get(el) === !!incoming) return true;   // lagging frame carrying the stale checked
    map.delete(el);                                 // authoritative response — release the guard
    return false;
}

// Third-party head preservation. Libraries commonly inject <style>/<link>/<script> into <head> at
// runtime (Monaco's theme colours, Chart.js, syntax highlighters, analytics). Those nodes aren't in the
// .NET-rendered head, so a naive reconcile would trim them on the next render — the framework already
// exposes data-rask-managed to opt a node out, but foreign libraries can't be expected to tag what they
// inject. Instead the morph tags every head node it PRODUCES (a __raskHead property set inline as each
// rendered node is placed) and, on later head morphs, skips any head element it never produced — leaving
// the foreign node in place exactly like a data-rask-managed one.
//
// Two invariants make this safe:
//   * The `raskHeadReconciled` gate keeps the FIRST head morph byte-identical to before, so boot-shell
//     hydration (importmap/base/preload/scoped placeholders) reconciles exactly as it used to.
//   * data-rask-key nodes are NEVER treated as foreign, so the framework's own keyed head nodes — most
//     importantly the scoped-CSS FOUC preload clone (rask-scoped.js), whose __raskHead expando does not
//     survive cloneNode — still reconcile by key instead of duplicating.
//
// Because ownership is marked inline on exactly the nodes derived from the render tree (not by a post-hoc
// sweep of all children), a sibling that a rendered inline <script> injects mid-morph is left unmarked and
// therefore preserved, rather than adopted-as-owned and trimmed on the following render.
let raskHeadReconciled = false;

function morph(from, to) {
    if (from.nodeType !== to.nodeType || from.nodeName !== to.nodeName) {
        _raskReplaceChild(from.parentNode, to, from);
        return;
    }
    if (from.nodeType === 3 || from.nodeType === 8) {
        if (from.nodeValue !== to.nodeValue) from.nodeValue = to.nodeValue;
        return;
    }
    const fa = from.attributes, ta = to.attributes;
    // Reverse walk: removeAttribute mutates the live `fa` NamedNodeMap, so iterate
    // by index from the end to keep the unvisited slots stable.
    for (let i = fa.length - 1; i >= 0; i--) {
        const name = fa[i].name;
        if (!to.hasAttribute(name)) from.removeAttribute(name);
    }
    for (const a of ta) {
        if (from.getAttribute(a.name) !== a.value) from.setAttribute(a.name, a.value);
    }
    const tag = from.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA") {
        // Only inputs with data-rask-on-input stream keystrokes — those need the
        // focus guard so a lagging re-render doesn't clobber mid-typed characters.
        // Change-only inputs (date / number / time / datetime-local / checkbox /
        // radio) commit at change time; the rendered value is canonical and must
        // win, otherwise Chromium leaves a focused date input's dirty value flag
        // stale and the first picker change appears to be dropped.
        const streaming = from.hasAttribute("data-rask-on-input") || to.hasAttribute("data-rask-on-input");
        if (!streaming || document.activeElement !== from) {
            let newVal = to.getAttribute("value");
            if (newVal === null && to.tagName === "TEXTAREA") newVal = to.textContent;
            // No rendered `value` (an <input> with no `value` attribute) means the input is
            // *uncontrolled* — the framework isn't managing its value, so a re-render (including a
            // full-document morph on a full reply — scoped-CSS delivery, reconnect, …) must leave the
            // user's typed DOM value alone rather than reset it to "". A controlled/bound input always
            // renders a `value` attribute (even `value=""`), so it still syncs below.
            // raskShouldSuppressValue runs first so it can clear a confirmed echo even when from.value
            // already equals newVal; a still-pending user edit (incoming !== the committed value) is
            // left untouched.
            if (newVal !== null && !raskShouldSuppressValue(from, newVal) && from.value !== newVal) {
                from.value = newVal;
            }
            // raskShouldSuppressChecked runs first (like the value guard) so a confirmed echo can
            // clear the guard even when from.checked already matches — a lagging frame carrying the
            // pre-click checked is left to the browser's just-applied native state.
            const checked = to.hasAttribute("checked");
            if (!raskShouldSuppressChecked(from, checked) && from.checked !== checked) from.checked = checked;
        }
    }
    // Skip JS-owned elements (marked data-rask-managed) — they're not part of
    // the .NET render tree, so pairing them against the incoming children would
    // either trim them off or replace them with something unrelated. Used by
    // the Server overlay (reconnect spinner sibling of <html>) and the WASM
    // scoped-css / scoped-js bundle tags (head children that don't appear in
    // the .NET-rendered HTML payload).
    // Foreign-head preservation (see the note above raskHeadReconciled): once the head has been
    // reconciled at least once, pull out any head element the morph never produced (a third-party lib
    // injected it since the last render) so it's left in place, exactly like a data-rask-managed node.
    // data-rask-key nodes are NOT foreign — they're framework keyed nodes (e.g. the scoped-CSS FOUC
    // clone) that must reconcile by key rather than duplicate.
    const isHead = from.nodeName === "HEAD";
    const skipForeign = isHead && raskHeadReconciled;
    // Tag a node the morph produces as Rask-owned (head only) so later morphs don't mistake it for a
    // foreign injection. Applied inline to exactly the nodes derived from the render tree.
    const own = isHead ? (n) => { n.__raskHead = true; return n; } : (n) => n;
    const fc = [], tc = [];
    for (let n = from.firstChild; n; n = n.nextSibling) {
        if (n.nodeType === 1 && n.hasAttribute("data-rask-managed")) continue;
        if (skipForeign && n.nodeType === 1 && n.__raskHead !== true && !n.hasAttribute("data-rask-key")) {
            continue;
        }
        fc.push(n);
    }
    for (let m = to.firstChild; m; m = m.nextSibling) tc.push(m);

    // Keyed reconciliation: if any incoming child carries data-rask-key, match
    // by key instead of by position so reordered list items keep their DOM
    // identity (focus, scroll, animations, ::part state) across re-renders.
    // Falls back to the positional walk below when no keys are present.
    let keyed = false;
    for (const node of tc) {
        if (node.nodeType === 1 && node.getAttribute && node.getAttribute("data-rask-key") !== null) {
            keyed = true;
            break;
        }
    }
    if (keyed) {
        const keyMap = new Map();
        const unkeyedFrom = [];
        for (const fn of fc) {
            const fk = (fn.nodeType === 1 && fn.getAttribute) ? fn.getAttribute("data-rask-key") : null;
            if (fk !== null) keyMap.set(fk, fn);
            else unkeyedFrom.push(fn);
        }
        let unkeyedCursor = 0;
        // Sentinel: keep the place we want to insert before. As we move/create
        // keyed nodes we advance this past the just-placed node; unkeyed nodes
        // follow the same anchor.
        let anchor = (fc.length > 0) ? fc[0] : null;
        for (const dst of tc) {
            const dk = (dst.nodeType === 1 && dst.getAttribute) ? dst.getAttribute("data-rask-key") : null;
            let src;
            if (dk !== null) {
                src = keyMap.get(dk) || null;
                if (src) keyMap.delete(dk);
            } else {
                src = unkeyedFrom[unkeyedCursor++] || null;
            }
            if (src === null) {
                _raskInsertBefore(from, own(reviveScript(dst)), anchor);
            } else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) {
                _raskInsertBefore(from, own(reviveScript(dst)), anchor);
                // If the from-node we're about to remove IS the anchor, advance the anchor past it
                // first — otherwise the next insert/move would pass a reference node no longer in
                // `from` and insertBefore throws "reference node is not a child". This happens when a
                // keyed sibling promotes the container to keyed reconciliation but some from-side
                // children don't match the new tree by node name (e.g. the SDK-injected <head>
                // importmap / <base> a WASM app hydrates against on a static host).
                if (src === anchor) anchor = anchor.nextSibling;
                _raskRemoveChild(from, src);
            } else {
                if (src !== anchor) _raskMoveBefore(from, src, anchor);
                else anchor = anchor.nextSibling;
                morph(src, dst);
                own(src);
            }
        }
        // Drop any from-side keyed nodes that were not claimed by the new tree.
        keyMap.forEach((n) => {
            if (n.parentNode === from) _raskRemoveChild(from, n);
        });
        // Drop trailing unkeyed nodes too.
        while (unkeyedCursor < unkeyedFrom.length) {
            const leftover = unkeyedFrom[unkeyedCursor++];
            if (leftover.parentNode === from) _raskRemoveChild(from, leftover);
        }
        if (isHead) raskHeadReconciled = true;
        return;
    }

    const max = Math.max(fc.length, tc.length);
    for (let k = 0; k < max; k++) {
        const src = fc[k], dst = tc[k];
        if (!src) _raskAppendChild(from, own(reviveScript(dst)));
        else if (!dst) _raskRemoveChild(from, src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) _raskReplaceChild(from, own(reviveScript(dst)), src);
        else { morph(src, dst); own(src); }
    }
    if (isHead) raskHeadReconciled = true;
}


// ----- Scoped-CSS FOUC gating: CSS_FOUC_GUARD_MS + waitForUnappliedHeadCss (diff path) +
//       preloadNewHeadStylesheets (full-HTML path) — Rask.Core/Resources/rask-scoped.js -----
// rask-scoped.js — scoped-CSS FOUC (flash-of-unstyled-content) gating, shared by all three clients.
//
// Spliced (at "// @@RASK_SCOPED@@") into the Server runtime (rask.js), the WASM runtime
// (rask.wasm.js) and the native runtime (rask.native.js). A newly mounted component ships its
// scoped stylesheet as a keyed <link href="/_rask/a/{hash}.css" data-rask-key="rsk-…">; without
// this gate the swapped body paints before that just-inserted sheet parses + applies, flashing
// unstyled. Both entry points return a Promise the host chains its render commit on (or null when
// there's nothing new to wait for, preserving today's single-pass timing).
//
// Relies only on the global `document` + standard timers — no transport coupling. Modern-ES
// (const/let/arrow), matching rask-dom.js / rask-morph.js. No export/import, no backslash regex.
//
// NOTE: the scoped-JS `Rask.*` invoke gate (trackHeadAsset / ensureRaskNamespacePoll /
// beginInvokeJS deferral) is deliberately NOT here — it has genuinely diverged between the Server
// (skips rsk- assets, 5s timeout) and WASM (tracks rsk- scripts, 30s backstop) hosts, so it stays
// inline per host until a dedicated reconciliation pass. See docs/native.md roadmap.

// Hard cap on how long a render defers the body swap waiting for a newly mounted page's scoped
// stylesheet to apply. A warm, content-addressed /_rask/a/{hash}.css load resolves in a few ms;
// the cap only ever applies to a genuinely slow/failed sheet, where we'd rather show the (briefly
// unstyled) page than stall navigation.
const CSS_FOUC_GUARD_MS = 500;

// Return a Promise that resolves once every <head> stylesheet still being applied has
// reached a terminal state (load / error / CSS_FOUC_GUARD_MS timeout), or null when
// there's nothing to wait for. The readiness signal is the <link>'s .sheet property —
// non-null only once the CSSOM stylesheet has been parsed and APPLIED. We deliberately
// do NOT use Resource Timing (responseEnd): the eager <link rel="prefetch"> warms the
// HTTP cache and creates a timing entry, but bytes downloaded is not the same as a
// stylesheet applied — trusting it would skip the wait and reintroduce the very flash
// prefetch is meant to remove. A link already applied (kept across renders, or just
// resolved) has a non-null .sheet and is skipped; a freshly inserted one has
// .sheet === null and is awaited (its load fires within ~1 frame on warm cache).
function waitForUnappliedHeadCss() {
    const pending = [];
    document.head.querySelectorAll('link[rel="stylesheet"]').forEach((l) => {
        if (!l.href || l.sheet) return;
        pending.push(new Promise((resolve) => {
            const done = () => resolve();
            l.addEventListener("load", done, {once: true});
            l.addEventListener("error", done, {once: true});
            setTimeout(done, CSS_FOUC_GUARD_MS);
        }));
    });
    return pending.length ? Promise.all(pending) : null;
}

// FOUC guard for the full-document path. A full reply morphs <head> and the styled <body> in one
// pass, so a newly mounted component's scoped <link> would be inserted alongside the body it styles
// — and the body paints before the just-inserted sheet parses + applies. Pre-empt it: for every NEW
// scoped stylesheet the incoming document adds to <head> (keyed by data-rask-key, so not already
// live), append a clone NOW and return a Promise that resolves once each has applied (.sheet) —
// load / error / CSS_FOUC_GUARD_MS timeout. The subsequent morph matches each clone to the incoming
// <link> by key (keyed reconciliation), so it's kept rather than duplicated, and the body it morphs
// in paints already-styled. Only keyed scoped links are preloaded — render-blocking globals (no
// data-rask-key) are already applied. Returns null when the document adds no new scoped stylesheet
// (the common case), so a navigation that mounts nothing new keeps today's single-pass, no-wait timing.
function preloadNewHeadStylesheets(freshHtml) {
    const freshHead = freshHtml.querySelector("head");
    if (!freshHead) return null;
    const liveKeys = {};
    document.head.querySelectorAll('link[rel="stylesheet"][data-rask-key]').forEach((l) => {
        liveKeys[l.getAttribute("data-rask-key")] = true;
    });
    const pending = [];
    freshHead.querySelectorAll('link[rel="stylesheet"][data-rask-key]').forEach((fl) => {
        if (liveKeys[fl.getAttribute("data-rask-key")] || !fl.getAttribute("href")) return;
        const clone = fl.cloneNode(true);
        document.head.appendChild(clone);
        pending.push(new Promise((resolve) => {
            const done = () => resolve();
            clone.addEventListener("load", done, {once: true});
            clone.addEventListener("error", done, {once: true});
            setTimeout(done, CSS_FOUC_GUARD_MS);
        }));
    });
    return pending.length ? Promise.all(pending) : null;
}


// The "#fragment" of an intercepted nav-link click is stashed here on click and consumed on the
// matching push reply (scroll to the anchor, else the top). Kept for parity with the other clients.
let _pendingScrollHash = "";

function inRoot(el) {
    // Whether an event target is inside the Rask-managed root (so we don't hijack events on, e.g.,
    // a third-party widget mounted outside it). The native root is the whole document body.
    return !!el && (root ? root.contains(el) : true);
}

// ----- Extended GlobalEventHandlers (mouse/wheel/pointer/touch/clipboard/media/beforeinput) -----
// Needs send(payload) + inRoot(el) in scope (declared above). — Rask.Core/Resources/rask-events.js
// rask-events.js — the extended GlobalEventHandlers delegation, shared by both client runtimes.
//
// Spliced into the Server runtime (rask.js, at "// @@RASK_EVENTS@@") and the WASM runtime
// (rask.wasm.js) so the two clients can never drift. It relies only on three symbols that both hosts
// define in the surrounding scope: `send(payload)`, `inRoot(el)` and the global `document`.
//
// Model: one capture-phase document listener per event routes to the nearest ancestor carrying
// `data-rask-on-<event>`, then ships a per-category JSON payload tagged with that element's handler id.
// Capture phase is used so non-bubbling events (focus/blur) still reach the delegated listener. Click,
// scroll and input/change/submit keep their own dedicated listeners in each host (their coalescing /
// form / file behaviour is host-specific) — this file covers everything else: mouse, pointer, touch,
// wheel, focus, clipboard, the HTMLMediaElement events, AND (see the tail of this file) keyboard
// (keydown/keyup) + the four core drag events (dragstart/dragover/drop/dragend), which used to be
// hand-copied into each host. Kept ES5 (var/function) because it is spliced verbatim into all three
// hosts. Written defensively: every builder tolerates a partial event object.

// --- Per-category payload builders. Each maps a DOM event to the flat object its C# *EventArgs.FromJson
//     reads. Keys mirror the DOM property names so the readers stay one-liners. ---

/** Geometry + button + modifier state shared by every mouse/pointer event. */
function raskMouse(e) {
    return {
        button: e.button, buttons: e.buttons,
        clientX: e.clientX, clientY: e.clientY, screenX: e.screenX, screenY: e.screenY,
        pageX: e.pageX, pageY: e.pageY, offsetX: e.offsetX, offsetY: e.offsetY,
        movementX: e.movementX, movementY: e.movementY,
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    };
}

/** Mouse geometry + scroll deltas for the wheel event. */
function raskWheel(e) {
    var m = raskMouse(e);
    m.deltaX = e.deltaX; m.deltaY = e.deltaY; m.deltaZ = e.deltaZ; m.deltaMode = e.deltaMode;
    return m;
}

/** Mouse geometry + pointer-device fields. */
function raskPointer(e) {
    var m = raskMouse(e);
    m.pointerId = e.pointerId; m.width = e.width; m.height = e.height;
    m.pressure = e.pressure; m.tangentialPressure = e.tangentialPressure;
    m.tiltX = e.tiltX; m.tiltY = e.tiltY; m.twist = e.twist;
    m.pointerType = e.pointerType; m.isPrimary = e.isPrimary;
    return m;
}

/** Active-touch count + first-touch coordinates + modifiers. */
function raskTouch(e) {
    var list = (e.touches && e.touches.length) ? e.touches : e.changedTouches;
    var first = (list && list.length) ? list[0] : null;
    return {
        touchCount: e.touches ? e.touches.length : 0,
        clientX: first ? first.clientX : 0, clientY: first ? first.clientY : 0,
        pageX: first ? first.pageX : 0, pageY: first ? first.pageY : 0,
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    };
}

/** The plain-text clipboard payload, read while it's accessible during the event. */
function raskClipboard(e) {
    var text = "";
    try {
        var data = e.clipboardData || window.clipboardData;
        if (data) { text = data.getData("text") || ""; }
    } catch (err) { /* access blocked — leave text empty */ }
    return { text: text };
}

/** A snapshot of the media element's playback state (NaN/Infinity duration normalised to 0). */
function raskMedia(e) {
    var el = e.target || {};
    return {
        currentTime: el.currentTime || 0,
        duration: (el.duration && isFinite(el.duration)) ? el.duration : 0,
        paused: !!el.paused, ended: !!el.ended,
        volume: el.volume == null ? 1 : el.volume, muted: !!el.muted,
        playbackRate: el.playbackRate == null ? 1 : el.playbackRate
    };
}

/** The inserted text for beforeinput (surfaced to a Callback<string>). */
function raskBeforeInput(e) { return { value: e.data == null ? "" : e.data }; }

/** Parameterless events (focus/blur, drag/dragenter/dragleave, select/invalid/reset). */
function raskNone() { return {}; }

// --- The registration table. Each row is [eventName, payloadBuilder, preventDefault]. ---
var raskDomEvents = [
    ["dblclick", raskMouse, false], ["mousedown", raskMouse, false], ["mouseup", raskMouse, false],
    ["mousemove", raskMouse, false], ["mouseover", raskMouse, false], ["mouseout", raskMouse, false],
    ["contextmenu", raskMouse, true],
    ["wheel", raskWheel, false],
    ["pointerdown", raskPointer, false], ["pointerup", raskPointer, false], ["pointermove", raskPointer, false],
    ["pointerover", raskPointer, false], ["pointerout", raskPointer, false], ["pointercancel", raskPointer, false],
    ["touchstart", raskTouch, false], ["touchend", raskTouch, false], ["touchmove", raskTouch, false], ["touchcancel", raskTouch, false],
    ["focus", raskNone, false], ["blur", raskNone, false], ["focusin", raskNone, false], ["focusout", raskNone, false],
    ["drag", raskNone, false], ["dragenter", raskNone, false], ["dragleave", raskNone, false],
    ["copy", raskClipboard, false], ["cut", raskClipboard, false], ["paste", raskClipboard, false],
    ["beforeinput", raskBeforeInput, false], ["select", raskNone, false], ["invalid", raskNone, false], ["reset", raskNone, false],
    ["play", raskMedia, false], ["pause", raskMedia, false], ["playing", raskMedia, false], ["ended", raskMedia, false],
    ["timeupdate", raskMedia, false], ["volumechange", raskMedia, false], ["ratechange", raskMedia, false],
    ["durationchange", raskMedia, false], ["loadedmetadata", raskMedia, false],
    ["seeked", raskMedia, false], ["seeking", raskMedia, false], ["waiting", raskMedia, false]
];

raskDomEvents.forEach(function (spec) {
    var name = spec[0], build = spec[1], prevent = spec[2], attr = "data-rask-on-" + name;
    // passive when we never preventDefault — lets the browser keep scrolling/painting smoothly even
    // while a high-frequency handler (mousemove/touchmove/wheel) is attached.
    document.addEventListener(name, function (e) {
        var target = (e.target && e.target.closest) ? e.target.closest("[" + attr + "]") : null;
        if (!target || !inRoot(target)) { return; }
        if (prevent) { e.preventDefault(); }
        var msg = build(e);
        msg.id = target.getAttribute(attr);
        msg.type = name;
        send(msg);
    }, { capture: true, passive: !prevent });
});

// mouseenter/leave and pointerenter/leave don't propagate to ancestors (not even in the capture phase),
// so a delegated listener can't observe them. Simulate via the bubbling over/out events plus a
// relatedTarget boundary check: fire only when the pointer truly crossed the element's outer edge
// (relatedTarget outside the element), not when moving between its own descendants.
function raskEnterLeave(sourceEvent, name, build) {
    var attr = "data-rask-on-" + name;
    document.addEventListener(sourceEvent, function (e) {
        var target = (e.target && e.target.closest) ? e.target.closest("[" + attr + "]") : null;
        if (!target || !inRoot(target)) { return; }
        var related = e.relatedTarget;
        if (related && target.contains(related)) { return; }
        var msg = build(e);
        msg.id = target.getAttribute(attr);
        msg.type = name;
        send(msg);
    }, { capture: true, passive: true });
}

raskEnterLeave("mouseover", "mouseenter", raskMouse);
raskEnterLeave("mouseout", "mouseleave", raskMouse);
raskEnterLeave("pointerover", "pointerenter", raskPointer);
raskEnterLeave("pointerout", "pointerleave", raskPointer);

// ----- Drag & drop -----------------------------------------------------------
// HTML5 native DnD bound to parameterless C# handlers (same dispatch path as click). The dragged
// item's identity rides the handler's closure, not the payload, so messages carry only {id,type}.
// dragstart seeds dataTransfer so the drag is valid in Firefox; dragover must preventDefault on a
// drop target or the browser rejects the drop. The optional data-rask-on-dragover round-trip
// drives a server-rendered drop-target highlight — deduped to one message per hovered element.
// (drag/dragenter/dragleave are covered by the parameterless table above.)
var lastDragOverEl = null;

document.addEventListener("dragstart", function (e) {
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-dragstart]") : null;
    if (!t || !inRoot(t)) { return; }
    if (e.dataTransfer) {
        try {
            e.dataTransfer.setData("text/plain", "");
        } catch (err) { /* some browsers throw if setData is disallowed — ignore */ }
        e.dataTransfer.effectAllowed = "move";
    }
    lastDragOverEl = null;
    send({id: t.getAttribute("data-rask-on-dragstart"), type: "dragstart"});
});

document.addEventListener("dragover", function (e) {
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-drop], [data-rask-on-dragover]") : null;
    if (!t || !inRoot(t)) { return; }
    // preventDefault is what marks this element as a valid drop target.
    e.preventDefault();
    if (e.dataTransfer) { e.dataTransfer.dropEffect = "move"; }
    if (!t.hasAttribute("data-rask-on-dragover")) { return; }
    if (t === lastDragOverEl) { return; } // dedupe: only notify when the hovered target changes
    lastDragOverEl = t;
    send({id: t.getAttribute("data-rask-on-dragover"), type: "dragover"});
});

document.addEventListener("drop", function (e) {
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-drop]") : null;
    if (!t || !inRoot(t)) { return; }
    e.preventDefault();
    lastDragOverEl = null;
    send({id: t.getAttribute("data-rask-on-drop"), type: "drop"});
});

document.addEventListener("dragend", function (e) {
    lastDragOverEl = null;
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-on-dragend]") : null;
    if (!t || !inRoot(t)) { return; }
    send({id: t.getAttribute("data-rask-on-dragend"), type: "dragend"});
});

// ----- Keyboard --------------------------------------------------------------
// keydown/keyup dispatch to the nearest ancestor carrying a handler (focus-scoped, like click).
// Never preventDefault — a key handler composes with normal typing; the C# side decides what a key
// means. flushInputsNow() first (when present — rask-input.js is spliced ahead of this file) so an
// Enter-to-submit handler reads the value the user just typed, not the pre-flush one. Modifier flags
// + repeat ride along for shortcuts.
function raskSendKey(e, attr, type) {
    var t = (e.target && e.target.closest) ? e.target.closest("[" + attr + "]") : null;
    if (!t || !inRoot(t)) { return; }
    if (typeof flushInputsNow === "function") { flushInputsNow(); }
    send({
        id: t.getAttribute(attr), type: type,
        key: e.key, code: e.code, repeat: e.repeat,
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    });
}

document.addEventListener("keydown", function (e) { raskSendKey(e, "data-rask-on-keydown", "keydown"); });
document.addEventListener("keyup", function (e) { raskSendKey(e, "data-rask-on-keyup", "keyup"); });

// ----- Share (client-only) ---------------------------------------------------
// ShareButton emits data-rask-share="{json}". The share MUST run inside the click's own call stack so the
// browser's transient user activation is still live — a server round-trip would lose it, which is exactly
// why this is handled on the client and not dispatched to C#. In a native shell we upgrade to the injected
// native bridge (window.__raskNative, no activation needed); otherwise we fall back to navigator.share.
// Unsupported browsers (e.g. desktop Firefox) simply no-op.
document.addEventListener("click", function (e) {
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-share]") : null;
    if (!t || !inRoot(t)) { return; }
    var raw = t.getAttribute("data-rask-share");
    if (!raw) { return; }
    var nativeCaps = window.__raskNative;
    if (nativeCaps && nativeCaps.capabilities && nativeCaps.capabilities.indexOf &&
        nativeCaps.capabilities.indexOf("share") !== -1 && typeof nativeCaps.invoke === "function") {
        nativeCaps.invoke("share", raw);
        return;
    }
    if (navigator.share) {
        var data;
        try { data = JSON.parse(raw); } catch (err) { return; }
        // Fire in the gesture; swallow rejections (user cancel / unsupported payload).
        try { var p = navigator.share(data); if (p && p["catch"]) { p["catch"](function () {}); } } catch (err) {}
    }
});


// ----- Native transport primitives ------------------------------------------------------------

// Post a client→host message. Two platform bridges are supported so neither races page-script execution:
//   • iOS injects window.__raskSend at document-start (a WKUserScript) → a WKScriptMessageHandler.
//   • Android exposes window.__raskBridge.dispatch synchronously via WebView.addJavascriptInterface.
// Either forwards the JSON string to INativeWebView.OnMessage → NativeAppHost.RouteMessageAsync.
function send(payload) {
    try {
        const s = JSON.stringify(payload);
        if (typeof window.__raskSend === "function") {
            window.__raskSend(s);
        } else if (window.__raskBridge && typeof window.__raskBridge.dispatch === "function") {
            window.__raskBridge.dispatch(s);
        } else {
            console.error("[Rask.Native] no native send bridge (window.__raskSend / window.__raskBridge)");
        }
    } catch (e) {
        console.error("[Rask.Native] send failed", e);
    }
}

// Serialize frame application so a deferred body swap can't be overtaken by the next frame.
let _renderQueue = Promise.resolve();

// Called by the host with each rendered frame (a JSON string — the same {kind:"diff",ops} / {html}
// envelope the WASM client receives as bytes). Exposed on window.__raskNative for EvaluateJavaScript.
function applyRender(json) {
    if (!json) return;
    let reply;
    try {
        reply = (typeof json === "string") ? JSON.parse(json) : json;
    } catch (e) {
        console.error("[Rask.Native] applyRender: malformed payload", e);
        return;
    }
    handle(reply);
}

function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        _renderQueue = _renderQueue.then(() => applyDiffReply(reply), () => applyDiffReply(reply));
        return;
    }
    _renderQueue = _renderQueue.then(() => applyFullReply(reply), () => applyFullReply(reply));
}

function dispatchNativeInvoke(inv) {
    beginInvokeJS(
        String(inv.id),
        inv.identifier,
        typeof inv.argsJson === "string" ? inv.argsJson : null,
        typeof inv.resultType === "number" ? inv.resultType : 0,
        typeof inv.targetInstanceId === "number" ? String(inv.targetInstanceId) : "0");
}

// Reflect the host-authored route change in the WebView's own history/location. There is no visible
// address bar on a device, but the WebView still keeps a history stack — so this is what makes hardware
// Back / forward work (via the popstate listener below) and what drives URL-routed UI (e.g. a dialog
// routed at /todos/new, Navigator.SetQuery). Mirrors applyHistory in rask.js / rask.wasm.js; there's no
// base-path prefix on native (the app is served from the origin root).
function applyHistory(history) {
    if (!history || typeof history.url !== "string") return;
    let target = history.url;
    if (history.action === "replace") {
        window.history.replaceState({ rask: true }, "", target);
    } else {
        if (_pendingScrollHash) target += _pendingScrollHash;
        window.history.pushState({ rask: true }, "", target);
    }
    _pendingScrollHash = "";
}

function applyDiffReply(reply) {
    // Morph <head> FIRST so a newly mounted component's scoped <link> is present, then defer the
    // body ops until that stylesheet applies (waitForUnappliedHeadCss) so the swapped body never
    // paints unstyled (FOUC) — the same gating rask.js / rask.wasm.js do. Returns the wait Promise
    // so _renderQueue holds the next frame until the body has committed.
    const applyBody = () => {
        applyDiff(reply.ops, Array.isArray(reply.names) ? reply.names : null);
        applyHistory(reply.history);
        applyFrameInvokes(reply, dispatchNativeInvoke);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    if (typeof reply.head === "string") {
        const freshHead = new DOMParser().parseFromString(reply.head, "text/html").head;
        if (freshHead) {
            morph(document.head, freshHead);
            const wait = waitForUnappliedHeadCss();
            if (wait) return wait.then(applyBody);
        }
    }
    return applyBody();
}

function applyFullReply(reply) {
    let freshHtml = null;
    if (typeof reply.html === "string" && reply.html.length > 0) {
        freshHtml = new DOMParser().parseFromString(reply.html, "text/html").documentElement;
    }
    // FOUC guard: preload + await any new scoped stylesheet the incoming document adds so the morph
    // paints the styled body only once its sheet has applied (preloadNewHeadStylesheets). Returns
    // null — commit synchronously at today's timing — when the render mounts no new scoped CSS.
    const applyDom = () => {
        if (freshHtml) {
            morph(document.documentElement, freshHtml);
            root = document.querySelector("[data-rask-root]") || document.body;
        }
        applyHistory(reply.history);
        applyFrameInvokes(reply, dispatchNativeInvoke);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    if (freshHtml) {
        const wait = preloadNewHeadStylesheets(freshHtml);
        if (wait) return wait.then(applyDom);
    }
    return applyDom();
}

// ----- Primary event handlers (ported from rask.wasm.js) --------------------------------------

// Click — carries the modifier keys the framework surfaces as MouseModifiers.
document.addEventListener("click", function (e) {
    // Nav-link interception: a Rask <a data-rask-nav> click navigates in-app rather than loading a URL.
    const link = e.target && e.target.closest ? e.target.closest("a[data-rask-nav]") : null;
    if (link && inRoot(link)) {
        e.preventDefault();
        const url = new URL(link.href, document.baseURI);
        _pendingScrollHash = url.hash || "";
        if (typeof flushInputsNow === "function") flushInputsNow();
        send({ type: "navigate", path: url.pathname, query: url.search, replace: false });
        return;
    }
    const el = e.target && e.target.closest ? e.target.closest("[data-rask-on-click]") : null;
    if (!el || !inRoot(el)) return;
    if (typeof flushInputsNow === "function") flushInputsNow();
    send({
        id: el.getAttribute("data-rask-on-click"), type: "click",
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    });
});

// Hardware Back / forward: the WebView pops its own history entry (pushed by applyHistory), so ask the
// host to navigate to the now-current location and re-render it. `replace` so the reply's applyHistory
// re-syncs the entry instead of pushing a duplicate.
window.addEventListener("popstate", function () {
    if (typeof flushInputsNow === "function") flushInputsNow();
    send({ type: "navigate", path: location.pathname, query: location.search, replace: true });
});

// Input & scroll — rAF-coalesced dispatch shared with rask.js / rask.wasm.js (rask-input.js). This
// provides the `input` + `scroll` listeners and flushInputsNow(); the change/submit/click handlers
// flush through it so the host processes a pending coalesced input before the dependent action.
// flushInputsNow is a hoisted function, so the keyboard handler spliced above (@@RASK_EVENTS@@) can
// call it regardless of splice order.
// rask-input.js — rAF-coalesced input & scroll dispatch, shared by all three client runtimes.
//
// Spliced (at "// @@RASK_INPUT@@") into the Server runtime (rask.js), the WASM runtime
// (rask.wasm.js) and the native runtime (rask.native.js) so the three clients can never drift.
// It relies only on three symbols every host defines in the surrounding scope: `send(payload)`,
// `inRoot(el)` and the global `document` (plus the standard requestAnimationFrame/
// cancelAnimationFrame). This module MUST be spliced BEFORE rask-events.js, whose keyboard handler
// calls flushInputsNow().
//
// Written in modern-ES (const/let/arrow), matching rask-dom.js / rask-morph.js — the other shared
// modules already spliced into all three hosts. No export/import, no backslash regex literals (the
// splice is a raw string .Replace).

// Input events fire per keystroke — on fast typing that's 5–10 messages over the
// transport per second per input. Coalesce per-element with rAF: the same element typed into
// multiple times within one frame produces a single outgoing message carrying the latest value
// at flush time. The element itself is the de-duping key — multiple inputs in the same frame
// each get one message. flushInputsNow() is called at the top of every other event handler
// (change, submit, click, navigate, keydown) so the host always processes input events before
// the subsequent action that depends on them — without this, a change event triggered
// immediately after typing reaches the host BEFORE the coalesced input, and any validator the
// change kicks off reads the stale model value.
const inputPending = new Set();
let inputRaf = 0;

function flushInputs() {
    inputRaf = 0;
    inputPending.forEach((el) => {
        if (!el.isConnected) return;
        const id = el.getAttribute("data-rask-on-input");
        if (!id) return;
        send({id, type: "input", value: el.value});
    });
    inputPending.clear();
}

function flushInputsNow() {
    if (inputRaf) {
        cancelAnimationFrame(inputRaf);
        inputRaf = 0;
    }
    if (inputPending.size > 0) flushInputs();
}

function queueInput(el) {
    inputPending.add(el);
    if (!inputRaf) inputRaf = requestAnimationFrame(flushInputs);
}

document.addEventListener("input", (e) => {
    const t = e.target.closest("[data-rask-on-input]");
    if (!t || !inRoot(t)) return;
    // Inputs paired with data-rask-on-change need to dispatch SYNCHRONOUSLY: the change
    // event typically fires in the same task (Playwright fill, browser commit on blur),
    // and a downstream validator triggered by change reads the model state set by the
    // matching input. Coalescing the input would put the change event ahead of it on
    // the .NET dispatcher and the validator would observe stale state. Only standalone
    // input handlers (no change wired) get the rAF coalescing win.
    if (t.hasAttribute("data-rask-on-change")) {
        send({id: t.getAttribute("data-rask-on-input"), type: "input", value: t.value});
        return;
    }
    queueInput(t);
});

// scroll events don't bubble — listen in capture phase at the document level so we
// observe scroll on any descendant with [data-rask-on-scroll]. Coalesce bursts via
// rAF: one outgoing message per frame per element, even if scroll fires 5–10x.
const scrollPending = new Set();
let scrollRaf = 0;

function flushScroll() {
    scrollRaf = 0;
    scrollPending.forEach((el) => {
        if (!el.isConnected) return;
        const id = el.getAttribute("data-rask-on-scroll");
        if (!id) return;
        send({
            id,
            type: "scroll",
            scrollTop: el.scrollTop | 0,
            clientHeight: el.clientHeight | 0,
            scrollHeight: el.scrollHeight | 0
        });
    });
    scrollPending.clear();
}

document.addEventListener("scroll", (e) => {
    const t = e.target;
    if (!t || t.nodeType !== 1) return;
    if (!t.hasAttribute || !t.hasAttribute("data-rask-on-scroll")) return;
    if (!inRoot(t)) return;
    scrollPending.add(t);
    if (!scrollRaf) scrollRaf = requestAnimationFrame(flushScroll);
}, true);


// Change — report the control's value (checkbox → checked). Flush any pending coalesced input first
// so a change-triggered validator reads the freshly-typed value, not the pre-flush one.
function valueOf(el) {
    if (el.type === "checkbox") return el.checked ? "true" : "false";
    return el.value == null ? "" : String(el.value);
}
document.addEventListener("change", function (e) {
    const el = e.target;
    if (!el || !el.getAttribute) return;
    const id = el.getAttribute("data-rask-on-change");
    if (!id || !inRoot(el)) return;
    if (typeof flushInputsNow === "function") flushInputsNow();
    send({ id: id, type: "change", value: valueOf(el) });
});

// Submit — serialize the form fields into a flat { name: value } bag.
document.addEventListener("submit", function (e) {
    const form = e.target;
    if (!form || !form.getAttribute) return;
    const id = form.getAttribute("data-rask-on-submit");
    if (!id || !inRoot(form)) return;
    e.preventDefault();
    if (typeof flushInputsNow === "function") flushInputsNow();
    const data = {};
    const fd = new FormData(form);
    fd.forEach(function (v, k) { if (typeof v === "string") data[k] = v; });
    send({ id: id, type: "submit", form: data });
});

// ----- IJSRuntime interop (host → JS), ported from rask.wasm.js ---------------------------------

const jsObjectRefs = new Map();
let nextJsObjectRefId = 1;

function jsResolveIdentifier(target, identifier) {
    if (typeof identifier !== "string" || identifier.length === 0) return null;
    const parts = identifier.split(".");
    let parent = target;
    for (let i = 0; i < parts.length - 1; i++) {
        if (parent == null) return null;
        parent = parent[parts[i]];
    }
    if (parent == null) return null;
    return [parent, parts[parts.length - 1]];
}

function jsReviver(_key, value) {
    if (value && typeof value === "object") {
        if (typeof value.__jsObjectId === "number") return jsObjectRefs.get(value.__jsObjectId);
        if (typeof value.__raskRef__ === "string") {
            return document.querySelector(`[data-rask-ref="${CSS.escape(value.__raskRef__)}"]`);
        }
    }
    return value;
}

// Host calls this via EvaluateJavaScript (NativeJSRuntime.DispatchOutsideRender) and the frame-invoke
// path (dispatchNativeInvoke). Runs the identified function and posts the result back as a jsResult.
function beginInvokeJS(taskId, identifier, argsJson, resultType, targetInstanceId) {
    Promise.resolve().then(() => {
        const args = JSON.parse(argsJson || "[]", jsReviver);
        let target = window;
        const targetId = Number(targetInstanceId);
        if (targetId !== 0) {
            target = jsObjectRefs.get(targetId);
            if (!target) throw new Error("Unknown JS object reference: " + targetInstanceId);
        }
        const resolved = jsResolveIdentifier(target, identifier);
        if (!resolved) throw new Error("Could not find '" + identifier + "' on target");
        const fn = resolved[0][resolved[1]];
        return (typeof fn === "function") ? fn.apply(resolved[0], args) : fn;
    }).then((value) => {
        if (resultType === 3) { postJsResult(taskId, true, null); return; }        // void
        if (resultType === 1) {                                                     // JS object ref
            const refId = nextJsObjectRefId++;
            jsObjectRefs.set(refId, value);
            postJsResult(taskId, true, { "__jsObjectId": refId });
            return;
        }
        postJsResult(taskId, true, value === undefined ? null : value);
    }).catch((err) => postJsResult(taskId, false, null, (err && err.message) || String(err)));
}

function postJsResult(taskId, success, result, error) {
    send(success
        ? { type: "jsResult", id: Number(taskId), success: true, result: result }
        : { type: "jsResult", id: Number(taskId), success: false, error: error || "JS invocation failed" });
}

// ----- DotNet shim (window.DotNet, for JS-initiated [JSInvokable]) ------------------------------
const dotNetPending = new Map();
let nextDotNetCallId = 1;

window.DotNet = window.DotNet || {
    invokeMethodAsync(assemblyName, methodIdentifier, ...args) {
        const callId = String(nextDotNetCallId++);
        return new Promise((resolve, reject) => {
            dotNetPending.set(callId, { resolve, reject });
            send({
                type: "dotNetInvoke", callId: callId, assemblyName: assemblyName,
                methodIdentifier: methodIdentifier, dotNetObjectId: 0, argsJson: JSON.stringify(args)
            });
        });
    }
};

// Host calls this via EvaluateJavaScript (NativeJSRuntime.EndInvokeDotNet) to resolve a [JSInvokable].
function endDotNetInvoke(resultJson) {
    let msg;
    try { msg = JSON.parse(resultJson); } catch (e) { console.error("[Rask.Native] endDotNetInvoke bad JSON", e); return; }
    const pending = dotNetPending.get(msg.callId);
    if (!pending) return;
    dotNetPending.delete(msg.callId);
    if (msg.success) pending.resolve(msg.result);
    else pending.reject(new Error(msg.error || "DotNet invocation failed"));
}

// The host reaches applyRender/beginInvokeJS/endDotNetInvoke through EvaluateJavaScript. capabilities +
// invoke() are the native device-capability bridge the shared client uses (e.g. Shareable): invoke() posts
// a capability message the host routes to the registered service (IShare) — see NativeAppHost. On the Native
// host, sharing is always available, so it's advertised here; invoke() needs no user activation.
window.__raskNative = {
    applyRender, beginInvokeJS, endDotNetInvoke,
    capabilities: ["share"],
    invoke: function (component, data) { send({ type: "capability", component: component, data: data }); }
};

// Signal readiness so the host fires its first render only now (see NativeAppHost.RouteMessageAsync).
root = document.querySelector("[data-rask-root]") || document.body;
send({ type: "ready" });
