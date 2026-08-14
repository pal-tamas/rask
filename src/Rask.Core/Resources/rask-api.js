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

    // Storage persistence (driven by IStorageEstimator): whether the origin is exempt from eviction, and a
    // request to become so. persist() is a one-shot grant — Chromium decides from engagement heuristics
    // without prompting, Firefox shows a permission prompt. Both resolve false where unsupported.
    storagePersisted: async () => {
        if (!(navigator.storage && navigator.storage.persisted)) {
            return false;
        }
        return await navigator.storage.persisted();
    },
    storagePersist: async () => {
        if (!(navigator.storage && navigator.storage.persist)) {
            return false;
        }
        return await navigator.storage.persist();
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

    // Binary values (SetBytesAsync/GetBytesAsync) travel the interop boundary as base64 — the one
    // encoding that marshals identically on every host — but are decoded here so the object store
    // holds a real Uint8Array. Storing the base64 text instead would cost ~33% of the browser's
    // storage quota for every byte, which matters once the value is something like a database file.
    const toBytes = (base64) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    };

    const toBase64 = (value) => {
        const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
        // Chunked: String.fromCharCode.apply throws RangeError once the argument list gets long,
        // which for a database-sized value is not a hypothetical.
        const CHUNK = 0x8000;
        let binary = "";
        for (let i = 0; i < bytes.length; i += CHUNK) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + CHUNK));
        }
        return btoa(binary);
    };

    return {
        isSupported: () => "indexedDB" in window,
        open: (name) => open(name).then(() => undefined),
        set: (name, key, value) => run(name, "readwrite", (s) => s.put(value, key)).then(() => undefined),
        get: (name, key) => run(name, "readonly", (s) => s.get(key)).then((v) => (v === undefined ? null : v)),
        setBytes: (name, key, base64) =>
            run(name, "readwrite", (s) => s.put(toBytes(base64), key)).then(() => undefined),
        getBytes: (name, key) =>
            run(name, "readonly", (s) => s.get(key)).then((v) => (v === undefined || v === null ? null : toBase64(v))),
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

// Battery Status (driven by IBattery). getStatus reads navigator.getBattery() once; watch adds the
// level/charging change listeners under the C#-minted id and pushes each snapshot back via the shared
// window.DotNet.invokeMethodAsync shim (static [JSInvokable] BatteryInterop.Changed in Rask.Core). Shared
// here (not WASM-only): navigator.getBattery needs no user gesture, so it works over the Server client too.
window.__raskBattery = window.__raskBattery || (() => {
    const watches = new Map(); // id -> {mgr, handler}
    const EVENTS = ["levelchange", "chargingchange", "chargingtimechange", "dischargingtimechange"];
    // charging/discharging time are Infinity when unknown — JSON can't carry Infinity, so map it to null.
    const read = (b) => ({
        level: b.level,
        charging: b.charging,
        chargingTime: isFinite(b.chargingTime) ? b.chargingTime : null,
        dischargingTime: isFinite(b.dischargingTime) ? b.dischargingTime : null
    });
    return {
        isSupported: () => typeof navigator.getBattery === "function",
        getStatus: () => navigator.getBattery ? navigator.getBattery().then(read) : Promise.resolve(null),
        watch: (id) => {
            if (!navigator.getBattery) {
                return Promise.resolve();
            }
            return navigator.getBattery().then((b) => {
                const handler = () =>
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskBatteryChanged", id, read(b));
                EVENTS.forEach((e) => b.addEventListener(e, handler));
                watches.set(id, {mgr: b, handler: handler});
            });
        },
        clear: (id) => {
            const w = watches.get(id);
            if (!w) {
                return;
            }
            watches.delete(id);
            EVENTS.forEach((e) => w.mgr.removeEventListener(e, w.handler));
        }
    };
})();

// Speech Recognition (driven by ISpeechRecognition) — webkitSpeechRecognition. start() builds the
// recognizer under the C#-minted id, wires onresult to push each result back via the shared
// window.DotNet.invokeMethodAsync shim (static [JSInvokable] SpeechRecognitionInterop.Result in Rask.Core),
// and begins listening; stop() ends it and releases the mic. Chromium-family only; the first start prompts
// for microphone access.
window.__raskSpeechRecognition = window.__raskSpeechRecognition || (() => {
    const sessions = new Map();
    const ctor = () => window.SpeechRecognition || window.webkitSpeechRecognition;
    return {
        isSupported: () => !!ctor(),
        start: (id, options) => {
            const C = ctor();
            if (!C) {
                return;
            }
            const rec = new C();
            if (options.lang) {
                rec.lang = options.lang;
            }
            rec.continuous = !!options.continuous;
            rec.interimResults = !!options.interimResults;
            rec.onresult = (e) => {
                for (let i = e.resultIndex; i < e.results.length; i++) {
                    const r = e.results[i];
                    const alt = r[0];
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskSpeechResult", id, {
                        transcript: alt ? alt.transcript : "",
                        isFinal: !!r.isFinal,
                        confidence: alt && isFinite(alt.confidence) ? alt.confidence : 0
                    });
                }
            };
            rec.onerror = (e) => {
                // A permission/service error is terminal — don't let onend restart into a loop.
                if (e && (e.error === "not-allowed" || e.error === "service-not-allowed")) {
                    const s = sessions.get(id);
                    if (s) {
                        s.stopped = true;
                    }
                }
            };
            rec.onend = () => {
                // webkitSpeechRecognition stops on silence; in continuous mode restart until stop() is called.
                const s = sessions.get(id);
                if (s && s.continuous && !s.stopped) {
                    try {
                        rec.start();
                    } catch (e) {
                        void e; // already (re)starting — ignore
                    }
                }
            };
            sessions.set(id, {rec: rec, continuous: !!options.continuous, stopped: false});
            rec.start();
        },
        stop: (id) => {
            const s = sessions.get(id);
            if (!s) {
                return;
            }
            s.stopped = true;
            sessions.delete(id);
            try {
                s.rec.stop();
            } catch (e) {
                void e; // not started — ignore
            }
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

// Origin Private File System (driven by IOriginPrivateFileSystem). OPFS handles are opaque and can't cross
// the interop boundary, and every call needs the path walked from the private root, so this helper resolves
// "db/app.sqlite" itself per operation rather than holding handles under an id the way __raskFs does — the
// tree is app-owned and persistent, so there is nothing to keep alive between calls. Bytes ride the boundary
// base64-encoded. A missing path resolves to null / false / [] rather than throwing.
window.__raskOpfs = window.__raskOpfs || (() => {
    // A path segment that isn't there, or is a directory where a file was expected.
    const isMissing = (e) => e && (e.name === "NotFoundError" || e.name === "TypeMismatchError");

    // Splits on "/" via split rather than a regex: the framework's JS minifier reads a bare /.../ as
    // division, which would break the spliced bundle.
    const segments = (path) => (path || "").split("/").filter((s) => s.length > 0);

    // "db/app.sqlite" -> { dir: <handle for "db">, name: "app.sqlite" }.
    const parent = async (path, create) => {
        const parts = segments(path);
        if (parts.length === 0) {
            return null;
        }
        const name = parts.pop();
        let dir = await navigator.storage.getDirectory();
        for (let i = 0; i < parts.length; i++) {
            dir = await dir.getDirectoryHandle(parts[i], {create: !!create});
        }
        return {dir: dir, name: name};
    };

    const fileHandle = async (path, create) => {
        const at = await parent(path, create);
        if (!at) {
            return null;
        }
        return await at.dir.getFileHandle(at.name, {create: !!create});
    };

    const directory = async (path) => {
        let dir = await navigator.storage.getDirectory();
        const parts = segments(path);
        for (let i = 0; i < parts.length; i++) {
            dir = await dir.getDirectoryHandle(parts[i], {create: false});
        }
        return dir;
    };

    const toBase64 = (buffer) => {
        const bytes = new Uint8Array(buffer);
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };

    const fromBase64 = (base64) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };

    return {
        isSupported: () => !!(navigator.storage && navigator.storage.getDirectory),
        exists: async (path) => {
            try {
                return !!(await fileHandle(path, false));
            } catch (e) {
                if (isMissing(e)) {
                    return false;
                }
                throw e;
            }
        },
        size: async (path) => {
            try {
                const handle = await fileHandle(path, false);
                if (!handle) {
                    return null;
                }
                return (await handle.getFile()).size;
            } catch (e) {
                if (isMissing(e)) {
                    return null;
                }
                throw e;
            }
        },
        // Blob.slice() reads only the requested range, so a chunked read never materialises the whole file.
        // A range past the end yields the bytes that were there, matching an ordinary short read.
        read: async (path, offset, count) => {
            try {
                const handle = await fileHandle(path, false);
                if (!handle) {
                    return null;
                }
                const file = await handle.getFile();
                return toBase64(await file.slice(offset, offset + count).arrayBuffer());
            } catch (e) {
                if (isMissing(e)) {
                    return null;
                }
                throw e;
            }
        },
        readAll: async (path) => {
            try {
                const handle = await fileHandle(path, false);
                if (!handle) {
                    return null;
                }
                return toBase64(await (await handle.getFile()).arrayBuffer());
            } catch (e) {
                if (isMissing(e)) {
                    return null;
                }
                throw e;
            }
        },
        // keepExistingData is load-bearing: without it createWritable() starts from an empty file, so a
        // ranged write would discard every byte outside the range it wrote. Writing past the end zero-fills
        // the gap rather than failing — File System Standard, write() step 9 — which is what lets a growing
        // database write a page beyond its current size.
        write: async (path, offset, base64) => {
            const handle = await fileHandle(path, true);
            const writable = await handle.createWritable({keepExistingData: true});
            await writable.write({type: "write", position: offset, data: fromBase64(base64)});
            await writable.close();
        },
        // Whole-file replace, so the default (start empty) is what we want here.
        writeAll: async (path, base64) => {
            const handle = await fileHandle(path, true);
            const writable = await handle.createWritable();
            await writable.write(fromBase64(base64));
            await writable.close();
        },
        truncate: async (path, size) => {
            const handle = await fileHandle(path, true);
            const writable = await handle.createWritable({keepExistingData: true});
            await writable.truncate(size);
            await writable.close();
        },
        delete: async (path, recursive) => {
            try {
                const at = await parent(path, false);
                if (!at) {
                    return;
                }
                await at.dir.removeEntry(at.name, {recursive: !!recursive});
            } catch (e) {
                if (isMissing(e)) {
                    return;
                }
                throw e;
            }
        },
        list: async (path) => {
            try {
                const names = [];
                for await (const name of (await directory(path)).keys()) {
                    names.push(name);
                }
                return names;
            } catch (e) {
                if (isMissing(e)) {
                    return [];
                }
                throw e;
            }
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

// Gesture-bridge DOM helpers — moved here from rask-wasm-api.js so they ship to the Server client too.
// They drive activation-gated browser APIs that must run inside a click gesture; the declarative
// FullscreenTrigger / EyeDropperTrigger components (and the data-rask-gesture click handler in
// rask-events.js) call these synchronously in the gesture, which is why they work even on the Server
// transport. The imperative IFullscreen / IEyeDropper services (WASM-only) call the same helpers.

// Fullscreen: with no element the whole page goes fullscreen (document.documentElement); exit is a no-op
// when nothing is fullscreen.
window.__raskFullscreen = window.__raskFullscreen || {
    isSupported: () => !!document.fullscreenEnabled,
    isActive: () => document.fullscreenElement != null,
    request: (el) => (el || document.documentElement).requestFullscreen(),
    exit: () => document.fullscreenElement ? document.exitFullscreen() : Promise.resolve()
};

// EyeDropper: open() resolves to the picked colour (#rrggbb); the picker rejects with AbortError when the
// user cancels (Escape) — map that to null rather than surfacing an error.
window.__raskEyeDropper = window.__raskEyeDropper || {
    isSupported: () => "EyeDropper" in window,
    open: () => new EyeDropper().open().then((r) => r.sRGBHex, () => null)
};

// Screen Orientation (driven by IScreenOrientation + the declarative ScreenOrientationTrigger). Reading
// returns the live screen.orientation as a plain { type, angle } object (mapped to OrientationInfo in C#);
// lock/unlock pass through. lock() only works while fullscreen, so the orientation.lock gesture cap enters
// fullscreen first. Shared here (not WASM-only) so the trigger reaches it on the Server client too.
window.__raskOrientation = window.__raskOrientation || {
    isSupported: () => "orientation" in screen,
    get: () => ({ type: screen.orientation.type, angle: screen.orientation.angle }),
    lock: (type) => screen.orientation.lock(type),
    unlock: () => { screen.orientation.unlock(); }
};

// PWA install prompt (driven by IInstallPrompt + the declarative InstallTrigger). The browser fires
// beforeinstallprompt once when the app becomes installable; we stash the event so it can be replayed from a
// user gesture (a custom "Install" button). This ships to every client (WASM and Server), so it must NOT
// preventDefault() — that would globally suppress the browser's own install affordance for apps that never
// use InstallTrigger. It isn't needed anyway: the mini-infobar was removed in Chrome 76, and the deferred
// event's prompt() replays fine without it. The listeners attach when this IIFE first runs at boot, so the
// event isn't missed. (Installability still needs a manifest + service worker over HTTPS — AddRaskPwa on Server.)
window.__raskInstall = window.__raskInstall || (() => {
    let deferred = null;
    let installed = false;
    window.addEventListener("beforeinstallprompt", (e) => {
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

// Media Capture / getUserMedia (driven by IMediaDevices + the declarative MediaCaptureTrigger). The live
// MediaStream can't cross interop, so each is held here under a JS-minted id; the video element is resolved
// from an ElementRef (imperative service) or the gesture bridge (declarative trigger). Stopping a stream
// stops every track, releasing the camera/mic (and its hardware indicator). Shared here (not WASM-only) so
// the trigger reaches it on the Server client too; getUserMedia still needs a secure (HTTPS) context.
window.__raskMedia = window.__raskMedia || (() => {
    const streams = new Map();
    let nextId = 0;
    const put = (stream) => {
        const id = ++nextId;
        streams.set(id, stream);
        return id;
    };
    const stop = (id) => {
        const stream = streams.get(id);
        if (!stream) {
            return;
        }
        stream.getTracks().forEach((t) => t.stop());
        streams.delete(id);
    };
    return {
        isSupported: () => !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
        enumerate: async () => {
            const devices = await navigator.mediaDevices.enumerateDevices();
            return devices.map((d) => ({deviceId: d.deviceId, kind: d.kind, label: d.label, groupId: d.groupId}));
        },
        getUserMedia: async (c) => {
            const video = c.video
                ? (c.facingMode ? {facingMode: c.facingMode} : true)
                : false;
            const stream = await navigator.mediaDevices.getUserMedia({audio: !!c.audio, video: video});
            return put(stream);
        },
        getDisplayMedia: async () => put(await navigator.mediaDevices.getDisplayMedia({video: true})),
        attach: (id, video) => {
            const stream = streams.get(id);
            if (!stream || !video) {
                return Promise.resolve();
            }
            video.srcObject = stream;
            video.muted = true;
            return video.play();
        },
        stop: (id) => stop(id)
    };
})();

// Picture-in-Picture (driven by IPictureInPicture + the declarative PictureInPictureTrigger). The element
// arg is a live <video> (resolved from an ElementRef by the imperative service, or from the gesture
// bridge's data-rask-ref for the trigger); exit is a no-op when no miniplayer is open. Shared here (not
// WASM-only) so the trigger reaches it on the Server client too.
window.__raskPip = window.__raskPip || {
    isSupported: () => !!document.pictureInPictureEnabled,
    isActive: () => document.pictureInPictureElement != null,
    request: (el) => el ? el.requestPictureInPicture() : Promise.reject(new Error("no video element")),
    exit: () => document.pictureInPictureElement ? document.exitPictureInPicture() : Promise.resolve()
};

// Web Locks (driven by IWebLocks) — coordinate work across the tabs/workers of one origin. C# mints the
// id and holds the lock by deferring release() until its `work` callback finishes: navigator.locks.request
// keeps the lock for the lifetime of the promise its callback returns, so we resolve `request` as soon as
// the lock is granted (or false when ifAvailable can't grant it) and park the held promise's resolver under
// the id until release(id) fires. Shared here (not WASM-only): navigator.locks needs no user gesture, so it
// works over the Server client too.
window.__raskLocks = window.__raskLocks || (() => {
    const releasers = new Map(); // id -> resolve() of the held promise
    return {
        isSupported: () => !!(navigator.locks && navigator.locks.request),
        request: (id, name, mode, ifAvailable) =>
            new Promise((resolveGranted, rejectGranted) => {
                const opts = {mode: mode || "exclusive"};
                if (ifAvailable) {
                    opts.ifAvailable = true;
                }
                navigator.locks.request(name, opts, (lock) => {
                    if (!lock) {
                        resolveGranted(false); // ifAvailable and the lock was already held
                        return undefined;
                    }
                    resolveGranted(true);
                    // Hold the lock until C# calls release(id); its promise stays pending until then.
                    return new Promise((release) => releasers.set(id, release));
                }).catch((e) => {
                    releasers.delete(id);
                    rejectGranted(e);
                });
            }),
        release: (id) => {
            const release = releasers.get(id);
            if (release) {
                releasers.delete(id);
                release();
            }
        },
        query: () => {
            if (!navigator.locks || !navigator.locks.query) {
                return Promise.resolve([]);
            }
            return navigator.locks.query().then((state) => {
                const out = [];
                (state.held || []).forEach((l) => out.push({name: l.name, mode: l.mode, clientId: l.clientId, held: true}));
                (state.pending || []).forEach((l) => out.push({name: l.name, mode: l.mode, clientId: l.clientId, held: false}));
                return out;
            });
        }
    };
})();

// WebRTC (driven by IWebRtc) — an RTCPeerConnection and its data channels can't cross interop, so each is
// held here under an id: C# mints connection ids (it must register its handlers before ICE gathering
// starts), JS mints channel ids (a remote peer can open one at any time, so one minting side keeps the id
// space single). Shared here (not WASM-only): none of this needs a user gesture, so it works over the
// Server client too.
//
// Everything pushed back to C# is BATCHED, and that is load-bearing rather than an optimisation: on the
// Server host each push is one inbound WebSocket frame, and RaskServerLimits.MaxInboundFramesPerSecond
// (1000 by default) closes the socket past it. A busy data channel or an ICE gathering burst would trip
// that in well under a second. Buffering on a fixed FLUSH_MS timer bounds the frame rate to ~60/s no
// matter how fast the peer sends. A timer, not requestAnimationFrame: rAF stops firing in a background
// tab, which would stall delivery exactly when a call is backgrounded.
//
// Message buffers are capped. Past MAX_BUFFERED the oldest are dropped and counted, and the count rides
// the next push so C# can surface the loss — an unbounded buffer would trade a closed socket for an
// out-of-memory tab. ICE candidates are never dropped (a lost candidate can cost connectivity, and a
// gathering burst is tens of entries, not thousands).
window.__raskRtc = window.__raskRtc || (() => {
    const conns = new Map(); // connId -> {pc, ice: [], timer: 0}
    const chans = new Map(); // chanId -> {ch, buf: [], dropped: 0, timer: 0, listening: false}
    let nextChan = 0;

    const FLUSH_MS = 16;
    const MAX_BUFFERED = 10000;

    const toBase64 = (buffer) => {
        const bytes = new Uint8Array(buffer);
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };

    const fromBase64 = (base64) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };

    const invoke = (method, ...args) => window.DotNet.invokeMethodAsync("Rask.Core", method, ...args);

    // A call against an already-disposed connection/channel would otherwise surface as a TypeError on
    // `undefined`, which says nothing about what the app did wrong.
    const conn = (id) => {
        const c = conns.get(id);
        if (!c) {
            throw new Error("Rask WebRTC: peer connection " + id + " is closed.");
        }
        return c;
    };

    const chan = (id) => {
        const c = chans.get(id);
        if (!c) {
            throw new Error("Rask WebRTC: data channel " + id + " is closed.");
        }
        return c;
    };

    const flushIce = (id) => {
        const c = conns.get(id);
        if (!c) {
            return;
        }
        c.timer = 0;
        if (c.ice.length === 0) {
            return;
        }
        const batch = c.ice;
        c.ice = [];
        invoke("RaskRtcIce", id, batch);
    };

    const flushMessages = (id) => {
        const c = chans.get(id);
        if (!c) {
            return;
        }
        c.timer = 0;
        if (!c.listening || c.buf.length === 0) {
            return;
        }
        const batch = c.buf;
        const dropped = c.dropped;
        c.buf = [];
        c.dropped = 0;
        // The connection id rides along because it is the one C# mints: a Server host runs many sessions
        // in one process, and channel ids minted here would collide across them.
        invoke("RaskRtcMessages", c.connId, id, batch, dropped);
    };

    const schedule = (c, run) => {
        if (c.timer === 0) {
            c.timer = setTimeout(run, FLUSH_MS);
        }
    };

    // Wires one channel — local or remote — into the id space and starts buffering immediately, so nothing
    // sent between "the channel exists" and "C# called listen" is lost.
    const adopt = (connId, ch) => {
        const id = ++nextChan;
        ch.binaryType = "arraybuffer";
        const state = {ch: ch, connId: connId, buf: [], dropped: 0, timer: 0, listening: false};
        chans.set(id, state);
        ch.onmessage = (e) => {
            if (state.buf.length >= MAX_BUFFERED) {
                state.buf.shift();
                state.dropped++;
            }
            state.buf.push(typeof e.data === "string"
                ? {text: e.data, data: null}
                : {text: null, data: toBase64(e.data)});
            schedule(state, () => flushMessages(id));
        };
        ch.onclose = () => invoke("RaskRtcChannelClosed", connId, id);
        return id;
    };

    const closeChannel = (id) => {
        const c = chans.get(id);
        if (!c) {
            return;
        }
        if (c.timer !== 0) {
            clearTimeout(c.timer);
        }
        chans.delete(id);
        c.ch.onmessage = null;
        c.ch.onclose = null;
        try {
            c.ch.close();
        } catch (_) {
            // Already closed with the connection — nothing to release.
        }
    };

    return {
        isSupported: () => typeof window.RTCPeerConnection === "function",
        create: (id, config) => {
            const servers = (config && config.iceServers ? config.iceServers : []).map((u) => ({urls: u}));
            const init = {iceServers: servers};
            if (config && config.iceTransportPolicy) {
                init.iceTransportPolicy = config.iceTransportPolicy;
            }
            const pc = new RTCPeerConnection(init);
            const state = {pc: pc, ice: [], timer: 0};
            conns.set(id, state);
            pc.onicecandidate = (e) => {
                // A null candidate marks end-of-gathering; flush what's buffered rather than forwarding it.
                if (!e.candidate) {
                    flushIce(id);
                    return;
                }
                state.ice.push({
                    candidate: e.candidate.candidate,
                    sdpMid: e.candidate.sdpMid,
                    sdpMLineIndex: e.candidate.sdpMLineIndex
                });
                schedule(state, () => flushIce(id));
            };
            pc.onconnectionstatechange = () => invoke("RaskRtcState", id, pc.connectionState);
            pc.ondatachannel = (e) => invoke("RaskRtcChannel", id, adopt(id, e.channel), e.channel.label);
        },
        createOffer: async (id) => {
            const c = conn(id);
            const offer = await c.pc.createOffer();
            return {type: offer.type, sdp: offer.sdp};
        },
        createAnswer: async (id) => {
            const c = conn(id);
            const answer = await c.pc.createAnswer();
            return {type: answer.type, sdp: answer.sdp};
        },
        setLocal: (id, d) => conn(id).pc.setLocalDescription({type: d.type, sdp: d.sdp}),
        setRemote: (id, d) => conn(id).pc.setRemoteDescription({type: d.type, sdp: d.sdp}),
        addIce: (id, cand) => conn(id).pc.addIceCandidate({
            candidate: cand.candidate,
            sdpMid: cand.sdpMid,
            sdpMLineIndex: cand.sdpMLineIndex
        }),
        createChannel: (connId, label, options) => {
            const init = {};
            if (options) {
                if (options.ordered != null) {
                    init.ordered = options.ordered;
                }
                if (options.maxRetransmits != null) {
                    init.maxRetransmits = options.maxRetransmits;
                }
                if (options.protocol) {
                    init.protocol = options.protocol;
                }
            }
            return adopt(connId, conn(connId).pc.createDataChannel(label, init));
        },
        // Starts delivery for a channel. Anything the peer sent before this point is already buffered and
        // rides the first push.
        listen: (id) => {
            const c = chans.get(id);
            if (!c) {
                return;
            }
            c.listening = true;
            schedule(c, () => flushMessages(id));
        },
        sendText: (id, text) => chan(id).ch.send(text),
        sendBytes: (id, base64) => chan(id).ch.send(fromBase64(base64)),
        closeChannel: (id) => closeChannel(id),
        close: (id) => {
            const c = conns.get(id);
            if (!c) {
                return;
            }
            if (c.timer !== 0) {
                clearTimeout(c.timer);
            }
            conns.delete(id);
            // Snapshot first: closeChannel deletes from the map we'd otherwise be iterating.
            const owned = [];
            chans.forEach((chan, chanId) => {
                if (chan.connId === id) {
                    owned.push(chanId);
                }
            });
            owned.forEach(closeChannel);
            c.pc.onicecandidate = null;
            c.pc.onconnectionstatechange = null;
            c.pc.ondatachannel = null;
            c.pc.close();
        }
    };
})();
