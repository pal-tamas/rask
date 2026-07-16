// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

let dotnetExports = null;
let root = null;
let basePath = null;

// Shared framework interop helpers (__raskEl, __raskApi) spliced from
// Rask.Core/Resources/rask-api.js at build time — single source across both transports.
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


// Transport-agnostic PWA helpers (__raskPush/__raskNotify/__raskBadge/__raskWakeLock) spliced from
// Rask.Core/Resources/rask-pwa.js — the same source the Server client uses.
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


// WASM-only helpers (__raskPwa.applyManifest, __raskInstall, device APIs) spliced from
// Rask.Wasm/Resources/rask-wasm-api.js — never ship in the Server client, since these back APIs that
// can't work over the WebSocket round-trip (or need WASM-only boot behaviour).
// WASM-only framework Web-API helpers, spliced into rask.wasm.js ONLY (by the RASK_WASM_API marker).
// These back APIs that can't work on the Server transport, so they must not ship in the Server
// client (rask.js) — keeping the Core shared rask-api.js / rask-pwa.js to genuinely-shared helpers only.
//
// The transport-agnostic PWA helpers (__raskPush, __raskNotify, __raskBadge, __raskWakeLock) live in
// Rask.Core/Resources/rask-pwa.js and are spliced into both clients; only the manifest injector and the
// install-prompt capture (which need page-side boot behaviour WASM provides) stay here.

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

// __raskInstall / __raskOrientation / __raskMedia / __raskPip moved to Rask.Core/Resources/rask-api.js so
// they also ship to the Server client — the declarative InstallTrigger / ScreenOrientationTrigger /
// MediaCaptureTrigger / PictureInPictureTrigger drive them inside the click gesture there (and __raskInstall
// must self-arm its beforeinstallprompt listener at boot on both transports). The imperative IInstallPrompt /
// IScreenOrientation / IMediaDevices / IPictureInPicture services stay WASM-only.

// __raskNotify / __raskBadge / __raskWakeLock are transport-agnostic and live in
// Rask.Core/Resources/rask-pwa.js (spliced into both clients) — they are not duplicated here.

// __raskFullscreen / __raskEyeDropper also moved to Rask.Core/Resources/rask-api.js (same reason — the
// declarative FullscreenTrigger / EyeDropperTrigger drive them on the Server client). The imperative
// IFullscreen / IEyeDropper services stay WASM-only.

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

// Web Serial (driven by ISerial). requestPort() needs transient activation and the live port stream, so
// this is WASM-only. C# mints the id and registers its callbacks BEFORE calling in here, so a device's first
// bytes can't race ahead of the handler. Each open port holds {port, reader, loop, closing, writeChain}
// under that id; the read loop pushes each inbound chunk back via window.DotNet.invokeMethodAsync (static
// [JSInvokable] SerialInterop.Data in Rask.Wasm — the WASM DotNet dispatcher resolves any assembly name).
// Bytes ride the boundary base64-encoded (btoa/atob, same as __raskFs): raw byte[] args don't marshal across
// the JS bridge. If the loop ends on its own (device unplugged / stream error) and it wasn't an explicit
// close(), we tear down and signal RaskSerialClosed so the UI can reset.
window.__raskSerial = window.__raskSerial || (() => {
    const ports = new Map();
    const toB64 = (bytes) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const read = (id, port) => {
        const entry = ports.get(id);
        const reader = port.readable.getReader();
        entry.reader = reader;
        entry.loop = (async () => {
            try {
                while (true) {
                    const {value, done} = await reader.read();
                    if (done) {
                        break;
                    }
                    if (value && value.length) {
                        window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskSerialData", id, toB64(value));
                    }
                }
            } catch (e) {
                // Device unplugged or stream error — fall through to teardown below.
            } finally {
                try { reader.releaseLock(); } catch (e) { /* already released */ }
            }
            // Natural end (not an explicit close): release the port and notify C# so it can fire onClosed.
            if (!entry.closing) {
                ports.delete(id);
                try { await port.close(); } catch (e) { /* already gone */ }
                window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskSerialClosed", id);
            }
        })();
    };
    return {
        isSupported: () => "serial" in navigator,
        requestPort: async (id, o) => {
            let port;
            try {
                // Drop null/absent ids so a vendor-only filter doesn't coerce to productId 0 (empty chooser).
                const filters = (o.filters || [])
                    .map((f) => {
                        const x = {};
                        if (f.usbVendorId != null) { x.usbVendorId = f.usbVendorId; }
                        if (f.usbProductId != null) { x.usbProductId = f.usbProductId; }
                        return x;
                    })
                    .filter((x) => Object.keys(x).length > 0);
                port = await navigator.serial.requestPort(filters.length ? {filters: filters} : {});
            } catch (e) {
                return false; // user dismissed the chooser
            }
            await port.open({
                baudRate: o.baudRate,
                dataBits: o.dataBits,
                stopBits: o.stopBits,
                parity: o.parity,
                bufferSize: o.bufferSize,
                flowControl: o.flowControl
            });
            ports.set(id, {port: port, reader: null, loop: null, closing: false, writeChain: Promise.resolve()});
            read(id, port);
            return true;
        },
        write: (id, data) => {
            const entry = ports.get(id);
            if (!entry) {
                return Promise.resolve();
            }
            // data is base64 (raw byte[] doesn't marshal); serialize writes so concurrent sends don't collide
            // on the single writable-stream lock.
            const bytes = fromB64(data);
            entry.writeChain = entry.writeChain.then(async () => {
                const writer = entry.port.writable.getWriter();
                try {
                    await writer.write(bytes);
                } finally {
                    writer.releaseLock();
                }
            });
            return entry.writeChain;
        },
        close: async (id) => {
            const entry = ports.get(id);
            if (!entry) {
                return;
            }
            entry.closing = true; // tell the read loop this end is deliberate (skip the teardown/notify path)
            ports.delete(id);
            if (entry.reader) {
                try { await entry.reader.cancel(); } catch (e) { /* already cancelled */ }
            }
            // Wait for the read loop to release the readable lock before closing, or close() rejects.
            if (entry.loop) {
                try { await entry.loop; } catch (e) { /* loop already ended */ }
            }
            try { await entry.port.close(); } catch (e) { /* already closed */ }
        }
    };
})();

// WebUSB (driven by IUsb). requestDevice() needs transient activation and the live device handle, so this is
// WASM-only. Each paired device is held under a framework-minted id (allocated JS-side); the same physical
// device reuses its id (idByDevice) so repeated getDevices() calls don't leak handles. Transfer payloads ride
// the boundary base64-encoded (btoa/atob, same as __raskFs): raw byte[] args don't marshal across the JS
// bridge. requestDevice maps only the NotFoundError dismissal to null (real errors propagate). A global
// disconnect listener evicts an unplugged device and signals RaskUsbDisconnected (static [JSInvokable]
// UsbInterop.Disconnected in Rask.Wasm) so the app can reset.
window.__raskUsb = window.__raskUsb || (() => {
    const byId = new Map();        // id -> USBDevice
    const idByDevice = new Map();  // USBDevice -> id (dedup + reverse lookup for disconnect)
    const refs = new Map();        // id -> open-handle refcount (the same device can back several C# handles)
    let nextId = 0;
    const toB64 = (bytes) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const dataB64 = (view) => view ? toB64(new Uint8Array(view.buffer, view.byteOffset, view.byteLength)) : "";
    const info = (d) => ({
        vendorId: d.vendorId,
        productId: d.productId,
        manufacturerName: d.manufacturerName || null,
        productName: d.productName || null,
        serialNumber: d.serialNumber || null
    });
    // The browser hands back the same USBDevice object from requestDevice/getDevices, so dedup to one id and
    // refcount it — a handle's close() only tears the device down once every C# handle to it has closed.
    const put = (device) => {
        let id = idByDevice.get(device);
        if (id === undefined) {
            id = ++nextId;
            byId.set(id, device);
            idByDevice.set(device, id);
            refs.set(id, 1);
        } else {
            refs.set(id, (refs.get(id) || 0) + 1);
        }
        return {id: id, info: info(device)};
    };
    const evict = (id) => {
        const device = byId.get(id);
        if (device) {
            byId.delete(id);
            idByDevice.delete(device);
        }
        refs.delete(id);
    };
    // A stale/closed id throws a clear error rather than an opaque "reading 'open' of undefined" TypeError.
    const dev = (id) => {
        const device = byId.get(id);
        if (!device) {
            throw new Error("USB device handle is closed or unknown (id " + id + ")");
        }
        return device;
    };
    if ("usb" in navigator && navigator.usb.addEventListener) {
        navigator.usb.addEventListener("disconnect", (e) => {
            const id = idByDevice.get(e.device);
            if (id !== undefined) {
                evict(id);
                window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskUsbDisconnected", id);
            }
        });
    }
    return {
        isSupported: () => "usb" in navigator,
        requestDevice: async (filters) => {
            let device;
            try {
                device = await navigator.usb.requestDevice({filters: filters || []});
            } catch (e) {
                if (e && e.name === "NotFoundError") {
                    return null; // user dismissed the chooser — not an error
                }
                throw e; // SecurityError, malformed filter, etc. — surface it
            }
            return put(device);
        },
        getDevices: async () => {
            if (!("usb" in navigator)) {
                return [];
            }
            return (await navigator.usb.getDevices()).map((d) => put(d));
        },
        open: (id) => dev(id).open(),
        selectConfiguration: (id, configurationValue) => dev(id).selectConfiguration(configurationValue),
        claimInterface: (id, interfaceNumber) => dev(id).claimInterface(interfaceNumber),
        releaseInterface: (id, interfaceNumber) => dev(id).releaseInterface(interfaceNumber),
        transferIn: async (id, endpointNumber, length) => {
            const r = await dev(id).transferIn(endpointNumber, length);
            return {status: r.status, data: dataB64(r.data)};
        },
        transferOut: async (id, endpointNumber, base64) => {
            const r = await dev(id).transferOut(endpointNumber, fromB64(base64));
            return {status: r.status, bytesWritten: r.bytesWritten};
        },
        controlTransferIn: async (id, setup, length) => {
            const r = await dev(id).controlTransferIn(setup, length);
            return {status: r.status, data: dataB64(r.data)};
        },
        controlTransferOut: async (id, setup, base64) => {
            const r = await dev(id).controlTransferOut(setup, fromB64(base64));
            return {status: r.status, bytesWritten: r.bytesWritten};
        },
        close: async (id) => {
            const device = byId.get(id);
            if (!device) {
                return;
            }
            const remaining = (refs.get(id) || 1) - 1;
            if (remaining > 0) {
                refs.set(id, remaining); // other C# handles still hold this device open
                return;
            }
            evict(id);
            try { await device.close(); } catch (e) { /* already closed */ }
        }
    };
})();

// WebHID (driven by IHid). requestDevice() needs transient activation and the live device handle, so this is
// WASM-only. Each paired device is held under a framework-minted id (deduped per physical device via
// idByDevice). Input reports are pushed via an inputreport listener that calls RaskHidInputReport (static
// [JSInvokable] HidInterop.Input in Rask.Wasm); a global disconnect listener evicts an unplugged device and
// calls RaskHidDisconnected. Report payloads ride the boundary base64-encoded (btoa/atob, same as __raskFs):
// raw byte[] args don't marshal across the JS bridge.
window.__raskHid = window.__raskHid || (() => {
    const byId = new Map();        // id -> HIDDevice
    const idByDevice = new Map();  // HIDDevice -> id (dedup + reverse lookup)
    const listeners = new Map();   // id -> inputreport listener (for removal)
    const refs = new Map();        // id -> open-handle refcount (the same device can back several C# handles)
    const watchCounts = new Map(); // id -> active watch count (one shared inputreport listener per device)
    let nextId = 0;
    const toB64 = (bytes) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const dataB64 = (view) => view ? toB64(new Uint8Array(view.buffer, view.byteOffset, view.byteLength)) : "";
    const info = (d) => ({vendorId: d.vendorId, productId: d.productId, productName: d.productName || null});
    // The browser hands back the same HIDDevice object from requestDevice/getDevices, so dedup to one id and
    // refcount it — a handle's close() only tears the device down once every C# handle to it has closed.
    const put = (device) => {
        let id = idByDevice.get(device);
        if (id === undefined) {
            id = ++nextId;
            byId.set(id, device);
            idByDevice.set(device, id);
            refs.set(id, 1);
        } else {
            refs.set(id, (refs.get(id) || 0) + 1);
        }
        return {id: id, info: info(device)};
    };
    const detach = (id) => {
        const device = byId.get(id);
        const listener = listeners.get(id);
        if (device && listener) {
            try { device.removeEventListener("inputreport", listener); } catch (e) { /* gone */ }
        }
        listeners.delete(id);
        watchCounts.delete(id);
    };
    const evict = (id) => {
        detach(id);
        const device = byId.get(id);
        if (device) {
            byId.delete(id);
            idByDevice.delete(device);
        }
        refs.delete(id);
    };
    // A stale/closed id throws a clear error rather than an opaque "reading 'open' of undefined" TypeError.
    const dev = (id) => {
        const device = byId.get(id);
        if (!device) {
            throw new Error("HID device handle is closed or unknown (id " + id + ")");
        }
        return device;
    };
    if ("hid" in navigator && navigator.hid.addEventListener) {
        navigator.hid.addEventListener("disconnect", (e) => {
            const id = idByDevice.get(e.device);
            if (id !== undefined) {
                evict(id);
                window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskHidDisconnected", id);
            }
        });
    }
    return {
        isSupported: () => "hid" in navigator,
        // navigator.hid.requestDevice resolves with [] on cancel (no rejection); real errors propagate.
        requestDevices: async (filters) => {
            if (!("hid" in navigator)) {
                return [];
            }
            return (await navigator.hid.requestDevice({filters: filters || []})).map((d) => put(d));
        },
        getDevices: async () => {
            if (!("hid" in navigator)) {
                return [];
            }
            return (await navigator.hid.getDevices()).map((d) => put(d));
        },
        open: (id) => dev(id).open(),
        close: async (id) => {
            const device = byId.get(id);
            if (!device) {
                return;
            }
            const remaining = (refs.get(id) || 1) - 1;
            if (remaining > 0) {
                refs.set(id, remaining); // other C# handles still hold this device open
                return;
            }
            evict(id);
            try { await device.close(); } catch (e) { /* already closed */ }
        },
        sendReport: (id, reportId, base64) => dev(id).sendReport(reportId, fromB64(base64)),
        sendFeatureReport: (id, reportId, base64) => dev(id).sendFeatureReport(reportId, fromB64(base64)),
        receiveFeatureReport: async (id, reportId) => dataB64(await dev(id).receiveFeatureReport(reportId)),
        watch: (id) => {
            const device = dev(id);
            const count = watchCounts.get(id) || 0;
            if (count === 0) {
                const listener = (e) => {
                    window.DotNet.invokeMethodAsync(
                        "Rask.Wasm", "RaskHidInputReport", id, e.reportId, dataB64(e.data));
                };
                listeners.set(id, listener);
                device.addEventListener("inputreport", listener);
            }
            watchCounts.set(id, count + 1);
        },
        unwatch: (id) => {
            const remaining = (watchCounts.get(id) || 0) - 1;
            if (remaining > 0) {
                watchCounts.set(id, remaining);
                return;
            }
            detach(id); // last watch — drop the shared inputreport listener
        }
    };
})();

// Web Bluetooth / GATT (driven by IBluetooth). requestDevice() needs transient activation and the live device
// handle, so this is WASM-only. Devices and resolved characteristics are deduped to one stable id each (the
// browser hands back the same object), so C# keeps one wrapper per physical handle — disconnect is reusable
// (gatt.disconnect), release() evicts. Notifications push RaskBluetoothValue (per characteristic id) and
// gattserverdisconnected pushes RaskBluetoothDisconnected (per device id) — both static [JSInvokable]s in
// Rask.Wasm. Values ride the boundary base64-encoded (btoa/atob, same as __raskFs): raw byte[] args don't
// marshal across the JS bridge. requestDevice rejects with NotFoundError on cancel.
window.__raskBluetooth = window.__raskBluetooth || (() => {
    const byId = new Map();          // deviceId -> BluetoothDevice
    const idByDevice = new Map();    // BluetoothDevice -> deviceId
    const chars = new Map();         // charId -> BluetoothRemoteGATTCharacteristic
    const charIdByObject = new Map();   // characteristic object -> charId (dedup so one id per physical char)
    const valueListeners = new Map();   // charId -> characteristicvaluechanged listener
    const notifyCounts = new Map();     // charId -> active notification-watch count
    const discListeners = new Map();    // deviceId -> gattserverdisconnected listener
    const discCounts = new Map();       // deviceId -> active disconnect-watch count
    let nextDeviceId = 0;
    let nextCharId = 0;
    const toB64 = (bytes) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const dataB64 = (view) => view ? toB64(new Uint8Array(view.buffer, view.byteOffset, view.byteLength)) : "";
    const info = (d) => ({id: d.id, name: d.name || null});
    const putDevice = (device) => {
        let id = idByDevice.get(device);
        if (id === undefined) {
            id = ++nextDeviceId;
            byId.set(id, device);
            idByDevice.set(device, id);
        }
        return {id: id, info: info(device)};
    };
    const dev = (id) => {
        const device = byId.get(id);
        if (!device) {
            throw new Error("Bluetooth device handle is closed or unknown (id " + id + ")");
        }
        return device;
    };
    const ch = (id) => {
        const characteristic = chars.get(id);
        if (!characteristic) {
            throw new Error("Bluetooth characteristic handle is unknown (id " + id + ")");
        }
        return characteristic;
    };
    const detachDisconnect = (deviceId) => {
        const device = byId.get(deviceId);
        const listener = discListeners.get(deviceId);
        if (device && listener) {
            try { device.removeEventListener("gattserverdisconnected", listener); } catch (e) { /* gone */ }
        }
        discListeners.delete(deviceId);
        discCounts.delete(deviceId);
    };
    return {
        isSupported: () => !!navigator.bluetooth,
        requestDevice: async (o) => {
            const opts = o.acceptAllDevices
                ? {acceptAllDevices: true}
                : {filters: o.filters || []};
            if (o.optionalServices && o.optionalServices.length) {
                opts.optionalServices = o.optionalServices;
            }
            let device;
            try {
                device = await navigator.bluetooth.requestDevice(opts);
            } catch (e) {
                if (e && e.name === "NotFoundError") {
                    return null; // user dismissed the chooser
                }
                throw e;
            }
            return putDevice(device);
        },
        getDevices: async () => {
            if (!navigator.bluetooth || !navigator.bluetooth.getDevices) {
                return [];
            }
            return (await navigator.bluetooth.getDevices()).map((d) => putDevice(d));
        },
        connect: async (id) => { await dev(id).gatt.connect(); },
        // Reusable: drops the GATT link but keeps the handle (reconnect with connect()). release() evicts.
        disconnect: (id) => {
            const device = byId.get(id);
            if (device) {
                try { device.gatt.disconnect(); } catch (e) { /* already disconnected */ }
            }
        },
        release: (id) => {
            const device = byId.get(id);
            if (!device) {
                return;
            }
            detachDisconnect(id);
            byId.delete(id);
            idByDevice.delete(device);
            try { device.gatt.disconnect(); } catch (e) { /* already disconnected */ }
        },
        isConnected: (id) => !!dev(id).gatt.connected,
        getCharacteristic: async (id, serviceUuid, characteristicUuid) => {
            const service = await dev(id).gatt.getPrimaryService(serviceUuid);
            const characteristic = await service.getCharacteristic(characteristicUuid);
            // Dedup to one id per physical characteristic so the notification refcount governs the shared
            // GATT subscription correctly (two handles can't silence each other).
            let charId = charIdByObject.get(characteristic);
            if (charId === undefined) {
                charId = ++nextCharId;
                chars.set(charId, characteristic);
                charIdByObject.set(characteristic, charId);
            }
            return charId;
        },
        readValue: async (charId) => dataB64(await ch(charId).readValue()),
        writeValue: async (charId, base64, withResponse) => {
            const characteristic = ch(charId);
            const bytes = fromB64(base64);
            if (withResponse && characteristic.writeValueWithResponse) {
                await characteristic.writeValueWithResponse(bytes);
            } else if (!withResponse && characteristic.writeValueWithoutResponse) {
                await characteristic.writeValueWithoutResponse(bytes);
            } else {
                await characteristic.writeValue(bytes); // older browsers
            }
        },
        startNotifications: async (charId) => {
            const characteristic = ch(charId);
            const count = notifyCounts.get(charId) || 0;
            if (count === 0) {
                const listener = (e) => {
                    window.DotNet.invokeMethodAsync(
                        "Rask.Wasm", "RaskBluetoothValue", charId, dataB64(e.target.value));
                };
                valueListeners.set(charId, listener);
                characteristic.addEventListener("characteristicvaluechanged", listener);
                await characteristic.startNotifications();
            }
            notifyCounts.set(charId, count + 1);
        },
        stopNotifications: async (charId) => {
            const remaining = (notifyCounts.get(charId) || 0) - 1;
            if (remaining > 0) {
                notifyCounts.set(charId, remaining);
                return;
            }
            notifyCounts.delete(charId);
            const characteristic = chars.get(charId);
            const listener = valueListeners.get(charId);
            valueListeners.delete(charId);
            if (characteristic && listener) {
                characteristic.removeEventListener("characteristicvaluechanged", listener);
                try { await characteristic.stopNotifications(); } catch (e) { /* already stopped / gone */ }
            }
        },
        releaseCharacteristic: (charId) => {
            // Drop the characteristic's id mapping (called when its C# handle is disposed); any live listener
            // is cleared too in case notifications weren't stopped first.
            const characteristic = chars.get(charId);
            const listener = valueListeners.get(charId);
            if (characteristic && listener) {
                try { characteristic.removeEventListener("characteristicvaluechanged", listener); } catch (e) { /* gone */ }
            }
            valueListeners.delete(charId);
            notifyCounts.delete(charId);
            chars.delete(charId);
            if (characteristic) {
                charIdByObject.delete(characteristic);
            }
        },
        watchDisconnect: (deviceId) => {
            const device = dev(deviceId);
            const count = discCounts.get(deviceId) || 0;
            if (count === 0) {
                const listener = () => {
                    window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskBluetoothDisconnected", deviceId);
                };
                discListeners.set(deviceId, listener);
                device.addEventListener("gattserverdisconnected", listener);
            }
            discCounts.set(deviceId, count + 1);
        },
        unwatchDisconnect: (deviceId) => {
            const remaining = (discCounts.get(deviceId) || 0) - 1;
            if (remaining > 0) {
                discCounts.set(deviceId, remaining);
                return;
            }
            detachDisconnect(deviceId);
        }
    };
})();


// Serializes render application across payloads. A navigation diff/full reply may defer
// its body swap until the new page's scoped CSS applies (waitForUnappliedHeadCss /
// preloadNewHeadStylesheets), opening a microtask/timer gap during which .NET could
// deliver the next render. Both
// the diff and full-HTML paths chain through this tail promise so a deferred body
// always commits before the following payload's ops — paths in a later diff are
// computed against the render this one produces, so they must not be applied first.
let _renderQueue = Promise.resolve();

// The "#fragment" of an intercepted nav-link click. The fragment never leaves the
// browser (the navigate message carries only path+query, and the history url has no
// hash), so we stash it here on click and consume it when the matching push reply
// commits — scroll to that anchor, else to the top. Cleared on consume.
let _pendingScrollHash = "";

// scopedJsReady starts true: per-component scripts ship as
// <script src="/_rask/a/{hash}.js" defer> tags in the initial HTML's <head> (and
// are morphed in/out as components mount/unmount). The browser's defer semantics
// run them before DOMContentLoaded, which is well before any user click could
// trigger a Rask.* invoke. The legacy bundle-based gate (waiting for one big
// inline-injected script) is gone with the cssText/jsText payload fields. The
// pendingScopedInvokes queue is kept because the user-Head-declared CDN path
// (see pendingHeadAssets below) still needs to defer Rask.* calls until those
// external deps have loaded.
let scopedJsReady = true;
let pendingScopedInvokes = [];

// External Head-declared <script src> and <link rel=stylesheet> are tracked
// here so Rask.* JS invokes can wait until every declared dep has reached
// a terminal state — load, error, OR a 5-second safety timeout — before
// firing. Without this, a component invoking e.g. window.hljs in its
// OnRenderedAsync would have to hand-roll its own load-event workaround.
// The gate is global on purpose: components don't know about each other's
// deps, and per-invoke dependency declarations push API surface back onto
// users.
//
// CONTRACT: the gate guarantees the asset's terminal event has fired
// before draining queued Rask.* invokes — NOT that the asset loaded
// successfully. A failed asset (CDN flake, refresh cache miss, extension
// block, integrity mismatch, CSP) still terminates the gate via its
// 'error' event or the 5s timeout, and queued invokes run anyway. User
// JS that depends on a global the asset was meant to define MUST be
// defensive — e.g. `if (typeof window.hljs === "undefined") return;`.
// The framework logs a clear warning on the failure paths so the
// resulting TypeError isn't a mystery.
const pendingHeadAssets = new Set();
const trackedHeadAssets = new WeakSet();
const failedHeadAssets = new Set();
const HEAD_ASSET_LOAD_TIMEOUT_MS = 5000;
// Scoped /_rask/a/{hash}.js scripts are same-origin and effectively always fire a
// load/error event, so they only need a hang-backstop, not the short user-CDN
// contract. The window must comfortably exceed how long a cold scoped-JS load can lag
// behind the first-render Rask.* invoke on a constrained 2-core runner — a deep-link
// straight to a CodeSample page queues Rask.CodeSample.rendered before the per-component
// <script defer> has executed, and a short window force-faults the invoke into
// "Could not find ... on target" so highlighting never lands. A genuinely-missing asset
// (404) still surfaces fast: its <script> fires an 'error' event that drains the gate
// immediately, so the long window only ever applies to a slow-but-loading asset.
const SCOPED_ASSET_LOAD_TIMEOUT_MS = 30000;
// CSS_FOUC_GUARD_MS + the scoped-CSS FOUC gating functions (waitForUnappliedHeadCss /
// preloadNewHeadStylesheets) are spliced in below from rask-scoped.js (@@RASK_SCOPED@@).

function isAssetAlreadyLoaded(url) {
    if (!url || !window.performance || !performance.getEntriesByName) return false;
    const entries = performance.getEntriesByName(url);
    for (let i = 0; i < entries.length; i++) {
        if (entries[i].responseEnd > 0) return true;
    }
    return false;
}

function trackHeadAsset(el) {
    if (!el || el.nodeType !== 1 || trackedHeadAssets.has(el)) return;
    // Per-component scoped tags carry data-rask-key with the framework-reserved
    // "rsk-" prefix, served from /_rask/a/{hash}.{ext}. Scoped CSS (<link rsk-css->)
    // never defines a JS global, so it stays out of the invoke gate. Scoped JS
    // (<script rsk-js->) DOES define window.Rask.{Type}; it must be tracked so a
    // first-render Rask.* invoke waits for the script's actual load event rather
    // than racing a fixed poll timeout — on a constrained runner the cold scoped-JS
    // load can lag well past that window, which previously force-faulted the call.
    const key = el.getAttribute("data-rask-key");
    const isScoped = !!(key && key.indexOf("rsk-") === 0);
    let url;
    if (el.tagName === "SCRIPT" && el.src) url = el.src;
    else if (el.tagName === "LINK" && el.rel === "stylesheet" && el.href) url = el.href;
    else return;
    if (isScoped && el.tagName !== "SCRIPT") return;
    // A same-origin asset (scoped /_rask/a/* OR a vendored user-Head script like a
    // self-hosted highlight.min.js) is reliable but can load slowly on a constrained
    // cold boot; it gets the generous hang-backstop. Only a true cross-origin CDN keeps
    // the short 5s contract (a dead CDN must not hold Rask.* invokes for 30s). A failed
    // same-origin asset still fires 'error' quickly, so the longer window only ever
    // applies to a genuinely slow-but-loading asset.
    const sameOrigin = typeof url === "string" && url.indexOf(location.origin) === 0;
    const useLongBackstop = isScoped || sameOrigin;
    trackedHeadAssets.add(el);
    // A scoped (rsk-) script must wait for its real load event before draining Rask.*
    // invokes: the eager <link rel="prefetch" as="script"> warms the HTTP cache and creates
    // a Resource Timing entry, but downloaded != executed — window.Rask.{Type} is only
    // defined once the script actually runs. Trusting timing here would let a first-render
    // invoke dispatch before execution and fault with "Could not find Rask.{Type}". For a
    // genuine warm non-scoped user-Head asset, "downloaded" stays an acceptable proxy (the
    // user's defensive code is the contract), so it keeps the fast path.
    if (!isScoped && isAssetAlreadyLoaded(url)) return;
    pendingHeadAssets.add(el);
    const finish = (outcome) => {
        if (!pendingHeadAssets.delete(el)) return;
        if (outcome === "error" || outcome === "timeout") {
            failedHeadAssets.add(url);
            const reason = outcome === "error"
                ? "fired 'error' event (network failure / blocked / integrity mismatch / CSP)"
                : `did not fire load/error within ${HEAD_ASSET_LOAD_TIMEOUT_MS}ms — proceeding anyway`;
            // console.warn rather than .error: the page CAN still render
            // (the user's defensive code is the contract). Surface enough
            // context that the consequent TypeError in user JS is traceable
            // back to the asset that failed.
            console.warn(`[Rask] Head asset (${el.tagName.toLowerCase()}) ${url} ${reason}. ` +
                "Queued Rask.* invokes will run; user JS depending on this asset's global must be defensive.");
        }
        maybeDrainPendingInvokes();
    };
    el.addEventListener("load", () => finish("load"), {once: true});
    el.addEventListener("error", () => finish("error"), {once: true});
    // Safety: the load/error event may have fired between insertion and our
    // listener attach (cache hit). The performance.getEntriesByName check
    // covers most cases; the timeout covers everything else so a missed
    // event doesn't hold Rask.* invokes forever. Same-origin assets get a generous
    // hang-backstop (a slow same-origin load is legitimate); cross-origin CDNs keep
    // the shorter contract.
    setTimeout(() => finish("timeout"), useLongBackstop ? SCOPED_ASSET_LOAD_TIMEOUT_MS : HEAD_ASSET_LOAD_TIMEOUT_MS);
}

function scanHeadAssets() {
    const els = document.head.querySelectorAll("script[src], link[rel=stylesheet]");
    for (let i = 0; i < els.length; i++) trackHeadAsset(els[i]);
}

function headAssetsReady() {
    return pendingHeadAssets.size === 0;
}

// Scoped-CSS FOUC gating: CSS_FOUC_GUARD_MS + waitForUnappliedHeadCss (diff path) +
// preloadNewHeadStylesheets (full-HTML path) — spliced from Rask.Core/Resources/rask-scoped.js,
// shared with rask.js + rask.native.js.
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


function maybeDrainPendingInvokes() {
    if (!scopedJsReady || !headAssetsReady()) return;
    if (pendingScopedInvokes.length === 0) return;
    // Re-queue any whose Rask.{Name} namespace still hasn't appeared — they'll be drained
    // by the polling loop below when (if) the per-component script eventually loads.
    const stillWaiting = [];
    const ready = [];
    for (let i = 0; i < pendingScopedInvokes.length; i++) {
        const c = pendingScopedInvokes[i];
        if (raskNamespaceReady(c.identifier)) ready.push(c);
        else stillWaiting.push(c);
    }
    pendingScopedInvokes = stillWaiting;
    for (let i = 0; i < ready.length; i++) {
        const c = ready[i];
        beginInvokeJS(c.taskId, c.identifier, c.argsJson, c.resultType, c.targetInstanceId);
    }
}

// Returns true when `Rask.{Name}` is populated on window (for "Rask.{Name}.{method}"
// identifiers), or true when the identifier doesn't follow the Rask.* pattern. Lets
// beginInvokeJS distinguish "the per-component script hasn't loaded yet — park me"
// from "ready to dispatch".
function raskNamespaceReady(identifier) {
    if (typeof identifier !== "string") return true;
    if (identifier.indexOf("Rask.") !== 0) return true;
    const rest = identifier.substring(5);
    const dot = rest.indexOf(".");
    const name = dot < 0 ? rest : rest.substring(0, dot);
    return !!(window.Rask && window.Rask[name]);
}

// Per-component scripts load asynchronously over HTTP from /_rask/a/{hash}.js. A first-
// render OnRenderedAsync calling Rask.X.method races the script's load event; the parked
// invoke needs a way to wake up when window.Rask.X appears. A 100ms poll catches the
// common cache-warm-load path and times out on genuinely-missing namespaces (those calls
// then surface "Could not find" as documented, rather than hanging forever).
//
// The timeout matches the scoped-asset load backstop (SCOPED_ASSET_LOAD_TIMEOUT_MS): on a
// constrained cold boot (e.g. the 2-core CI runner) the per-component bundle can execute
// several seconds after the first-render invoke is queued, and when its <script> isn't yet
// tracked as a pending head asset, headAssetsReady() is true — so a short 5s window would
// force-fault "Could not find 'Rask.X.method' on target" and trip RootErrorBoundary while
// the bundle was merely still loading. The longer window lets the namespace appear first.
const RASK_NAMESPACE_POLL_INTERVAL_MS = 100;
const RASK_NAMESPACE_POLL_TIMEOUT_MS = SCOPED_ASSET_LOAD_TIMEOUT_MS;
let raskNamespacePollHandle = 0;
let raskNamespacePollStarted = 0;

function ensureRaskNamespacePoll() {
    if (raskNamespacePollHandle !== 0) return;
    raskNamespacePollStarted = Date.now();
    raskNamespacePollHandle = setInterval(() => {
        const timedOut = Date.now() - raskNamespacePollStarted > RASK_NAMESPACE_POLL_TIMEOUT_MS;
        // Force-dispatch only once there's nothing left to wait for: the queue drained,
        // OR the poll timed out AND every tracked head/scoped asset has reached a
        // terminal state. The headAssetsReady() guard is what keeps a still-loading
        // scoped /_rask/a/{hash}.js from being faulted prematurely on a slow runner —
        // its load event drains the queue normally; a genuinely missing/errored
        // namespace still surfaces "Could not find" once its script terminates.
        if (pendingScopedInvokes.length === 0 || (timedOut && headAssetsReady())) {
            // Time's up: drain whatever's left through beginInvokeJS — the missing-namespace
            // calls will surface their original "Could not find" JSException, which the
            // component's ErrorBoundary catches. Better than hanging forever.
            clearInterval(raskNamespacePollHandle);
            raskNamespacePollHandle = 0;
            const drained = pendingScopedInvokes;
            pendingScopedInvokes = [];
            for (let i = 0; i < drained.length; i++) {
                const c = drained[i];
                dispatchUnparked(c.taskId, c.identifier, c.argsJson, c.resultType, c.targetInstanceId);
            }
            return;
        }
        maybeDrainPendingInvokes();
    }, RASK_NAMESPACE_POLL_INTERVAL_MS);
}

// Read once from <base href> (or the page URL if no <base> is set) so the
// runtime can host under a sub-path like /Rask/ on GitHub Pages without the
// .NET side ever seeing the prefix. Resolves to the directory portion so a
// page URL like /index.html yields "/" (not "/index.html/").
export function getBasePath() {
    if (basePath !== null) return basePath;
    const p = new URL(document.baseURI).pathname;
    const last = p.lastIndexOf("/");
    basePath = last < 0 ? "/" : p.slice(0, last + 1);
    return basePath;
}

function stripBase(pathname) {
    const b = getBasePath();
    if (b === "/" || !pathname) return pathname;
    if (pathname === b.slice(0, -1) || pathname === b) return "/";
    return pathname.startsWith(b) ? "/" + pathname.slice(b.length) : pathname;
}

function prependBase(url) {
    const b = getBasePath();
    if (b === "/" || typeof url !== "string" || !url.startsWith("/") || url.startsWith(b)) return url;
    return b + url.slice(1);
}

// Called from main.js once `getAssemblyExports` is available so the JS event
// handlers below can dispatch into .NET via the JSExport surface.
export function setExports(exports) {
    dotnetExports = exports;
    root = document.querySelector("[data-rask-root]") || document.body;
    const ok = !!(exports && exports.Rask && exports.Rask.Wasm
        && exports.Rask.Wasm.JSInterop && typeof exports.Rask.Wasm.JSInterop.Dispatch === "function");
    console.log("[Rask] setExports — Dispatch reachable:", ok, "root:", root && root.tagName);
    // Initial sweep for Head-declared external assets emitted by the browser's
    // index.html (and any subsequent applyRender will re-sweep so morph-added
    // assets get picked up too — see applyDom in handle()).
    scanHeadAssets();
}

// Called by .NET (via [JSImport]) for both the initial paint and subsequent
// background re-renders. `payload` is a MemoryView — a zero-copy view over the
// UTF-8 JSON frame in the C# write buffer (built via LivePayload.BuildPayloadUtf8WithRoot,
// same shape as the WS frame the server emits). `.slice()` materialises a Uint8Array
// copy on the JS side (the one unavoidable copy, replacing the prior per-frame byte[]
// the C# side used to allocate); TextDecoder + JSON.parse then run on that. `.slice()`
// is also valid on a Uint8Array, so this stays correct if ever called with one directly.
const _payloadDecoder = new TextDecoder("utf-8");

export function applyRender(payload) {
    if (!payload || payload.length === 0) return;
    let reply;
    try {
        reply = JSON.parse(_payloadDecoder.decode(payload.slice()));
    } catch (e) {
        console.error("[Rask] applyRender: malformed payload", e);
        return;
    }
    handle(reply);
}

// File registry for input[type=file]: maps short refs -> live File objects.
// Cleared when the file input fires another change so old refs become unreachable.
const fileRegistry = new Map();

export async function readFileChunk(ref, offset, length) {
    const file = fileRegistry.get(ref);
    if (!file) return new Uint8Array();
    const end = Math.min(file.size, offset + length);
    const slice = file.slice(offset, end);
    const buf = await slice.arrayBuffer();
    return new Uint8Array(buf);
}

function registerFiles(inputEl, files) {
    // Drop any prior refs for this input so a re-pick doesn't pile up File objects.
    if (inputEl.__raskFileRefs) {
        for (const r of inputEl.__raskFileRefs) fileRegistry.delete(r);
    }
    const metas = [];
    const refs = [];
    for (const f of files) {
        const r = (crypto && crypto.randomUUID) ? crypto.randomUUID() : "f-" + Math.random().toString(36).slice(2);
        fileRegistry.set(r, f);
        refs.push(r);
        metas.push({
            ref: r,
            name: f.name,
            size: f.size,
            type: f.type || "application/octet-stream",
            lastModified: f.lastModified || 0
        });
    }
    inputEl.__raskFileRefs = refs;
    return metas;
}

function triggerDownload(download) {
    if (!download || typeof download.filename !== "string") return;
    let bytes;
    if (typeof download.token === "string" && download.token.length > 0
        && dotnetExports && dotnetExports.Rask && dotnetExports.Rask.Wasm
        && dotnetExports.Rask.Wasm.JSInterop
        && typeof dotnetExports.Rask.Wasm.JSInterop.PullDownload === "function") {
        // Token-pull path: bytes live in .NET, JSExport returns them directly as a Uint8Array.
        // No base64 inflation, no decode loop — render payload only carried the token string.
        bytes = dotnetExports.Rask.Wasm.JSInterop.PullDownload(download.token);
    } else if (typeof download.bytes === "string") {
        // Legacy base64-inline path (test seam + back-compat).
        bytes = decodeBase64(download.bytes);
    }
    if (!bytes || bytes.length === 0) return;
    const blob = new Blob([bytes], {type: download.contentType || "application/octet-stream"});
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = download.filename;
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
        try {
            document.body.removeChild(a);
        } catch (_) {
        }
        URL.revokeObjectURL(url);
    }, 0);
}

function decodeBase64(b64) {
    if (typeof b64 !== "string" || b64.length === 0) return null;
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
}

export function getLocation() {
    return stripBase(location.pathname) + location.search;
}

export function getBaseAddress() {
    // The app root (origin + base path), NOT document.baseURI. document.baseURI reflects the
    // *current* SPA route once the app has navigated (the <base> element is not in the live DOM
    // after boot), so reading it here would bake whatever route happened to be active when the
    // singleton HttpClient was first resolved into its BaseAddress — e.g. a fetch of
    // "data/posts-1.json" from a two-segment route like /guides/elements would resolve against
    // /guides/ and 404. getBasePath() is cached from the boot-time <base href> (carrying any
    // sub-path) and is route-independent, so the base stays the app root for the app's lifetime.
    return new URL(getBasePath(), location.origin).href;
}

export function pushHistory(url, replace) {
    const target = prependBase(url);
    if (replace) window.history.replaceState({rask: true}, "", target);
    else window.history.pushState({rask: true}, "", target);
}

function inRoot(el) {
    return root && root.contains(el);
}

// reviveScript() + morph() are concatenated in at build time by the
// _RaskSpliceClientJs target.
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

// Third-party <head> preservation. Libraries routinely inject <style>/<link>/<script> into <head> at
// runtime (a code editor's theme colours, a charting lib, a syntax highlighter, analytics). Those nodes
// aren't in the .NET-rendered head, so the reconciler below would trim them on the next head morph. Rather
// than change the reconciliation (its invariants — keyed FOUC clones, boot-shell hydration, self-healing —
// are load-bearing), we watch <head> and tag anything a library injects with data-rask-managed, which the
// reconciler ALREADY skips (see the fc-building loop). The framework's own head mutations happen during an
// apply (a head morph, or an applyDiff InsertSubtree of a Head-declared script/link); those are discarded
// from the observer queue so they're never mistaken for foreign. data-rask-key nodes (the framework's keyed
// head links, incl. the scoped-CSS FOUC preload clone) are never tagged — they must reconcile by key.
let _raskHeadObserver = null;
let _raskObservedHead = null;

function _raskEnsureHeadObserver() {
    if (typeof MutationObserver === "undefined" || typeof document === "undefined" || !document.head) {
        return;
    }
    // Already watching the live <head> — nothing to do.
    if (_raskHeadObserver && _raskObservedHead === document.head) {
        return;
    }
    // First install, or the <head> element was replaced (not morphed in place) — (re)arm on the live head.
    if (_raskHeadObserver) _raskHeadObserver.disconnect();
    _raskObservedHead = document.head;
    // The callback receives the pending records as its argument — do NOT call takeRecords() here (it would
    // return empty, since delivery already drained them). takeRecords() is only for the synchronous flush
    // at a head morph / applyDiff, where the records are still pending.
    _raskHeadObserver = new MutationObserver((records) => _raskTagHeadRecords(records));
    _raskHeadObserver.observe(_raskObservedHead, { childList: true });
}

// Tag the nodes added by these mutation records — a <style>/<link>/<script> a library injected — with
// data-rask-managed so the reconciler's skip preserves them. Never tags data-rask-key nodes (the
// framework's own keyed head links, e.g. the scoped-CSS FOUC clone, which must reconcile by key).
function _raskTagHeadRecords(records) {
    for (const r of records) {
        for (const n of r.addedNodes) {
            if (n.nodeType === 1 && !n.hasAttribute("data-rask-key") && !n.hasAttribute("data-rask-managed")) {
                n.setAttribute("data-rask-managed", "");
            }
        }
    }
}

// Synchronous flush at the start of a head morph: tag foreign nodes injected since the last drain that the
// async observer callback hasn't processed yet, so this morph preserves them.
function _raskTagForeignHeadNodes() {
    if (_raskHeadObserver) _raskTagHeadRecords(_raskHeadObserver.takeRecords());
}

// Drop the head mutations the framework itself just made (during a morph or applyDiff) so the async
// observer never tags framework-inserted head nodes as foreign. Called at the end of every head morph and
// at the end of applyDiff (rask-dom.js).
function _raskDiscardFrameworkHeadMutations() {
    if (_raskHeadObserver) _raskHeadObserver.takeRecords();
}

// Install eagerly when the client bundle loads, so a library that injects into <head> before the first
// head morph is still observed (the lazy install inside morph() is the fallback for when document.head
// isn't ready at load time). The observer only tags nodes ADDED after it arms — the boot-shell head is
// left alone.
_raskEnsureHeadObserver();

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
    // Reconciling the live <head>: before pairing children, tag anything a third-party library injected
    // (see the note above _raskHeadObserver) as data-rask-managed so the skip below preserves it. The
    // observer is installed lazily on the first head morph — library injections happen after boot.
    const isDocHead = typeof document !== "undefined" && from === document.head;
    if (isDocHead) {
        _raskEnsureHeadObserver();
        _raskTagForeignHeadNodes();
    }
    // Skip JS-owned elements (marked data-rask-managed) — they're not part of
    // the .NET render tree, so pairing them against the incoming children would
    // either trim them off or replace them with something unrelated. Used by
    // the Server overlay (reconnect spinner sibling of <html>) and the WASM
    // scoped-css / scoped-js bundle tags (head children that don't appear in
    // the .NET-rendered HTML payload).
    //
    // The filter is symmetric: an incoming (to-side) child carrying the marker is
    // always a misuse — a .NET-rendered node is by definition part of the payload,
    // so the marker contradicts itself. Skipping it makes that mistake a harmless
    // no-op; without this, the from-side node is filtered out but the to-side one
    // isn't, so every morph appends a fresh unpaired copy (unbounded DOM growth).
    const fc = [], tc = [];
    for (let n = from.firstChild; n; n = n.nextSibling) {
        if (n.nodeType === 1 && n.hasAttribute("data-rask-managed")) continue;
        fc.push(n);
    }
    for (let m = to.firstChild; m; m = m.nextSibling) {
        if (m.nodeType === 1 && m.hasAttribute("data-rask-managed")) continue;
        tc.push(m);
    }

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
                _raskInsertBefore(from, reviveScript(dst), anchor);
            } else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) {
                _raskInsertBefore(from, reviveScript(dst), anchor);
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
        if (isDocHead) _raskDiscardFrameworkHeadMutations();
        return;
    }

    const max = Math.max(fc.length, tc.length);
    for (let k = 0; k < max; k++) {
        const src = fc[k], dst = tc[k];
        if (!src) _raskAppendChild(from, reviveScript(dst));
        else if (!dst) _raskRemoveChild(from, src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) _raskReplaceChild(from, reviveScript(dst), src);
        else morph(src, dst);
    }
    if (isDocHead) _raskDiscardFrameworkHeadMutations();
}


function applyHistory(history) {
    if (!history || typeof history.url !== "string") return;
    let target = prependBase(history.url);
    if (history.action === "replace") {
        window.history.replaceState({rask: true}, "", target);
    } else {
        if (_pendingScrollHash) target += _pendingScrollHash;
        window.history.pushState({rask: true}, "", target);
    }
}

// Reset scroll on forward navigation only (history.action "push" — a nav-link click
// or Navigator.Navigate). "replace" (Back/Forward popstate, SetQuery, auth redirect)
// is left to the browser's native scroll restoration. When the intercepted link
// carried a "#fragment" matching an element, scroll there instead of the top.
// Call this only after the new body has committed so the anchor target exists.
function applyNavScroll(history) {
    if (!history || history.action === "replace") {
        _pendingScrollHash = "";
        return;
    }
    const hash = _pendingScrollHash;
    _pendingScrollHash = "";
    if (hash && hash.length > 1) {
        let el = null;
        try {
            el = document.querySelector(hash) ||
                document.getElementById(decodeURIComponent(hash.slice(1)));
        } catch (e) {
            el = null;
        }
        if (el) {
            el.scrollIntoView();
            return;
        }
    }
    window.scrollTo(0, 0);
}

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

    // Symmetric with the discard below: tag any foreign head node injected before this diff (still pending,
    // not yet delivered to the async observer) so the end-of-diff discard only drops the framework's own
    // head insertions, never a coincidentally-pending library injection.
    _raskTagForeignHeadNodes();

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
    // A diff can insert Head-declared <script>/<link> into <head> (keyed InsertSubtree). Discard those
    // framework mutations from the head observer's queue so they aren't tagged as foreign injections
    // (see _raskHeadObserver in rask-morph.js).
    _raskDiscardFrameworkHeadMutations();
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
    // the filter — and moving the text caret in the editable date/time picker box — keeps working; the
    // picker's day cursor still moves on Left/Right (its C# handler runs regardless). Capture-phase so we
    // run before the browser commits the default action.
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


function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    // Diff-mode payload: apply ops directly against the live DOM. Both paths chain
    // through _renderQueue so a diff that defers its body for a CSS load can't be
    // overtaken by the next payload (see _renderQueue).
    if (reply.kind === "diff" && Array.isArray(reply.ops)) {
        _renderQueue = _renderQueue.then(() => applyDiffReply(reply), () => applyDiffReply(reply));
        return;
    }
    _renderQueue = _renderQueue.then(() => applyFullReply(reply), () => applyFullReply(reply));
}

// Per-invoke executor for the shared applyFrameInvokes loop (rask-dom.js). A frame's jsInvokes run
// AFTER applyDiff/morph patched the DOM — so a queued OnRenderedAsync focus acts on the committed
// DOM (e.g. a <dialog> that just gained its `open` attribute), the same post-commit ordering the
// Server has. beginInvokeJS runs the call and returns its result via the endInvokeJSResult JSExport.
function dispatchWasmInvoke(inv) {
    beginInvokeJS(
        String(inv.id),
        inv.identifier,
        typeof inv.argsJson === "string" ? inv.argsJson : null,
        typeof inv.resultType === "number" ? inv.resultType : 0,
        typeof inv.targetInstanceId === "number" ? String(inv.targetInstanceId) : "0");
}

function applyDiffReply(reply) {
    // The head isn't in the diff frame stream (user Head contributions are collected +
    // spliced render-side), so a head change rides the payload as a <head> fragment.
    // Morph it into document.head FIRST — keyed reconciliation (data-rask-key) keeps
    // unchanged scoped-CSS links, and morph skips data-rask-managed boot bundles so they
    // survive. When the new page adds a not-yet-cached scoped stylesheet, defer the body
    // ops until it loads so the swapped body never paints unstyled (FOUC).
    const applyBody = () => {
        applyDiff(reply.ops, Array.isArray(reply.names) ? reply.names : null);
        applyHistory(reply.history);
        applyNavScroll(reply.history);
        // A diff can insert Head-declared external <script>/<link> and scoped-JS tags
        // (keyed InsertSubtree). Track them so their load events feed the Rask.* invoke
        // gate, then drain anything now unblocked — the full-HTML morph path does the same.
        scanHeadAssets();
        maybeDrainPendingInvokes();
        applyFrameInvokes(reply, dispatchWasmInvoke);
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
    applyBody();
}

function applyFullReply(reply) {
    let freshHtml = null;
    if (typeof reply.html === "string" && reply.html.length > 0) {
        const doc = new DOMParser().parseFromString(reply.html, "text/html");
        // Morph the whole <html> element so head changes (title, stylesheet links,
        // scoped-css link) propagate too — the App component owns the full page,
        // not just <body>. The bootstrap <script src="main.js"> in the original
        // index.html may get removed by morph if the App's body doesn't include
        // an equivalent; that's harmless because the module is already running.
        freshHtml = doc.documentElement;
    }
    // All post-morph work (history push, scoped CSS/JS apply, scoped-JS dispatch,
    // raskAfterMorph hook) runs inside the applyDom callback so dispatch reads the
    // freshly-morphed DOM rather than the pre-morph one.
    const applyDom = () => {
        if (freshHtml) {
            morph(document.documentElement, freshHtml);
            root = document.querySelector("[data-rask-root]") || document.body;
            // Pick up any newly-inserted Head-declared external assets so
            // their load events feed into the Rask.* invoke gate.
            scanHeadAssets();
        }
        applyHistory(reply.history);
        // Cross-route navigation in WASM commits via this full-HTML morph (not the
        // diff path), so the scroll reset / fragment scroll must run here too — the
        // new body has just committed, so the anchor target exists.
        applyNavScroll(reply.history);
        // Scoped CSS/JS arrives in the morphed HTML as
        // <link href="/_rask/a/{hash}.css"> / <script src="/_rask/a/{hash}.js" defer>
        // tags — no payload-side cssText/jsText injection. Browser handles load
        // semantics via standard <link>/<script> lifecycle.
        applyFrameInvokes(reply, dispatchWasmInvoke);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
        if (reply.download) triggerDownload(reply.download);
    };
    // FOUC guard: preload any new scoped stylesheet the incoming document adds so the morph
    // paints the styled body only once its sheet has applied (see preloadNewHeadStylesheets).
    // Returns null — and we commit synchronously, at today's timing — when the render mounts
    // no new scoped CSS.
    if (freshHtml) {
        const wait = preloadNewHeadStylesheets(freshHtml);
        if (wait) return wait.then(applyDom);
    }
    applyDom();
}

// Cached at module scope: TextEncoder construction is cheap but not free, and a
// steady-typing user fires `send` ~60×/sec via the rAF input-coalescing path.
const _sendEncoder = new TextEncoder();

async function send(payload) {
    console.log("[Rask] send", payload);
    if (!dotnetExports) {
        console.warn("[Rask] send: dotnetExports not set");
        return;
    }
    if (!dotnetExports.Rask || !dotnetExports.Rask.Wasm || !dotnetExports.Rask.Wasm.JSInterop) {
        console.error("[Rask] send: Dispatch path missing on exports", dotnetExports);
        return;
    }
    try {
        // Dispatch now marshals the request as a byte[] (cuts the per-event UTF-16 string
        // copy across the JS/.NET boundary that the prior string signature forced) and
        // .NET pushes the response back through the existing applyRender JSImport — the
        // JSExport generator doesn't support Task<byte[]> return types. JS just awaits
        // completion; the morph happens via the applyRender callback path.
        const requestBytes = _sendEncoder.encode(JSON.stringify(payload));
        await dotnetExports.Rask.Wasm.JSInterop.Dispatch(requestBytes);
    } catch (e) {
        console.error("Rask: dispatch failed", e);
    }
}

document.addEventListener("click", (e) => {
    if (e.defaultPrevented) return;
    if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target.closest("a[data-rask-nav]");
    if (!a) return;
    console.log("[Rask] navlink click", a.getAttribute("href"));
    if (a.getAttribute("target") === "_blank") return;
    const href = a.getAttribute("href");
    if (!href) return;
    let url;
    try {
        url = new URL(href, location.href);
    } catch (_) {
        return;
    }
    if (url.origin !== location.origin) return;
    e.preventDefault();
    // Stash the link's "#fragment" so applyNavScroll can scroll to the anchor once
    // the new page commits (the fragment is not sent to the server).
    _pendingScrollHash = url.hash || "";
    flushInputsNow();
    send({type: "navigate", path: stripBase(url.pathname), query: url.search});
});

window.addEventListener("popstate", () => {
    flushInputsNow();
    send({type: "navigate", path: stripBase(location.pathname), query: location.search, replace: true});
});

document.addEventListener("click", (e) => {
    const t = e.target.closest("[data-rask-on-click]");
    if (!t || !inRoot(t)) return;
    // A submit/reset button is driven by native form submission (handled by the dedicated submit
    // listener). Don't let an ANCESTOR click handler (e.g. a modal's .modal-dialog shield) hijack it
    // and cancel the default — that would break the form submit. A handler on the button itself still
    // runs: note `button.type` defaults to "submit" for a bare <button>, so gating on the ancestor
    // (t !== btn) is what keeps a plain Button(OnClick:) working here. Mirrors rask.js.
    const btn = e.target.closest("button, input");
    if (btn && btn !== t && (btn.type === "submit" || btn.type === "reset")) return;
    e.preventDefault();
    flushInputsNow();
    send({
        id: t.getAttribute("data-rask-on-click"), type: "click",
        shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
    });
});

// rAF-coalesced input & scroll dispatch (inputPending/flushInputsNow/queueInput + the input and
// scroll listeners) — spliced from Rask.Core/Resources/rask-input.js, shared with rask.js +
// rask.native.js. MUST precede @@RASK_EVENTS@@ (its keyboard handler calls flushInputsNow).
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


document.addEventListener("change", (e) => {
    const t = e.target.closest("[data-rask-on-change], [data-rask-on-files]");
    if (!t || !inRoot(t)) return;
    // Flush before processing — if the same element (or a sibling) has a pending
    // coalesced input, the server needs to see it BEFORE the change-triggered
    // validator / handler runs, otherwise the validator reads stale model state.
    flushInputsNow();
    if (t.tagName === "INPUT" && t.type === "file" && t.hasAttribute("data-rask-on-files")) {
        const files = t.files;
        if (!files || files.length === 0) return;
        const metas = registerFiles(t, files);
        send({id: t.getAttribute("data-rask-on-files"), type: "files", files: metas});
        return;
    }
    if (t.hasAttribute("data-rask-on-change")) {
        // For a checkbox the meaningful state is el.checked, not el.value (the static "on"
        // default). Report it as "true"/"false" so bound checkboxes set the model to the
        // actual state (self-correcting). Radios/text keep sending el.value.
        const changeVal = (t.tagName === "INPUT" && t.type === "checkbox")
            ? (t.checked ? "true" : "false")
            : t.value;
        // Record the PRE-EDIT value (the last server-rendered `value` attribute) so a
        // lagging re-render carrying that stale value can't clobber the user's fresh
        // edit before the server's authoritative response lands — see
        // raskShouldSuppressValue. Checkboxes self-correct via the checked path, so
        // they stay out of the value guard.
        if (!(t.tagName === "INPUT" && t.type === "checkbox")) {
            const sv = t.getAttribute("value");
            raskNotePendingValue(t, sv === null ? "" : sv);
        }
        // Same guard for the `.checked` property: record the PRE-CLICK checked (the `checked`
        // attribute, which a native click leaves untouched) so a lagging re-render can't revert
        // the just-committed selection before the authoritative frame lands — see
        // raskShouldSuppressChecked. For a radio, note the whole same-name group: a stale frame that
        // re-checks the previously selected radio would natively uncheck the new one.
        if (t.tagName === "INPUT" && (t.type === "checkbox" || t.type === "radio")) {
            if (t.type === "radio" && t.name) {
                root.querySelectorAll('input[type=radio][name="' + CSS.escape(t.name) + '"]')
                    .forEach((r) => raskNotePendingChecked(r, r.hasAttribute("checked")));
            } else {
                raskNotePendingChecked(t, t.hasAttribute("checked"));
            }
        }
        send({id: t.getAttribute("data-rask-on-change"), type: "change", value: changeVal});
    }
});

document.addEventListener("submit", (e) => {
    const t = e.target.closest("[data-rask-on-submit]");
    if (!t || !inRoot(t)) return;
    e.preventDefault();
    flushInputsNow();
    const fileInputs = t.querySelectorAll('input[type="file"][name]');
    const fileFields = {};
    for (const input of fileInputs) {
        if (!input.files || input.files.length === 0) continue;
        fileFields[input.name] = registerFiles(input, input.files);
    }
    const fd = new FormData(t);
    const obj = {};
    fd.forEach((v, k) => {
        if (v instanceof File || v instanceof Blob) return;
        obj[k] = String(v);
    });
    if (Object.keys(fileFields).length > 0) obj.__files = fileFields;
    send({id: t.getAttribute("data-rask-on-submit"), type: "submit", form: obj});
});

// Extended GlobalEventHandlers delegation + keyboard (keydown/keyup) + the four core drag events
// (dragstart/dragover/drop/dragend) — spliced from Rask.Core/Resources/rask-events.js.
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

// ----- Gesture bridge (client-only) ------------------------------------------
// GestureTrigger / FullscreenTrigger / EyeDropperTrigger emit data-rask-gesture="{cap,rid}". The capability
// MUST run inside the click's own call stack so the browser's transient user activation is still live — a
// server round-trip would lose it. That's what lets activation-gated APIs (fullscreen, eyedropper, …) work
// even on the Server transport. When a result-callback id (rid) is set, the resolved value is posted back to
// C# via the shared DotNet shim (static [JSInvokable] GestureResultInterop.Result in Rask.Core).
// Each cap runs synchronously inside the click, given (arg, el): arg is the payload's optional string
// argument (orientation type, JSON media constraints), el the resolved target element (the <video> for
// picture-in-picture / media capture). A returned Promise's value is posted back when a rid is set.
var raskGestureCaps = {
    "fullscreen.request": function (arg, el) { return window.__raskFullscreen ? window.__raskFullscreen.request(el) : null; },
    "eyedropper.open": function () { return window.__raskEyeDropper ? window.__raskEyeDropper.open() : null; },
    "orientation.lock": function (arg) {
        // screen.orientation.lock only resolves while the page is fullscreen (and on a device that honours
        // it); off-fullscreen / on desktop it rejects, which the dispatcher swallows — a genuine silent
        // no-op. Pair with FullscreenTrigger (or app fullscreen) rather than forcing fullscreen here, which
        // would strand a desktop user in a fullscreen page with the orientation unchanged.
        return window.__raskOrientation ? window.__raskOrientation.lock(arg) : null;
    },
    "pip.request": function (arg, el) { return window.__raskPip ? window.__raskPip.request(el) : null; },
    "install.prompt": function () {
        return window.__raskInstall ? window.__raskInstall.prompt() : Promise.resolve("unavailable");
    },
    "media.start": function (arg, el) {
        if (!window.__raskMedia || !el) { return Promise.resolve("denied"); }
        var c;
        try { c = arg ? JSON.parse(arg) : {}; } catch (err) { c = {}; }
        return window.__raskMedia.getUserMedia(c).then(function (id) {
            // Await the attach/play so "granted" reflects a stream actually running in the <video>, not just
            // permission; a play() hiccup on a muted stream still counts as granted (permission was given).
            return Promise.resolve(window.__raskMedia.attach(id, el)).then(
                function () { return "granted"; }, function () { return "granted"; });
        }, function () { return "denied"; });
    }
};
function raskPostGestureResult(rid, value) {
    if (window.DotNet && window.DotNet.invokeMethodAsync) {
        window.DotNet.invokeMethodAsync("Rask.Core", "RaskGestureResult", rid, value == null ? null : value);
    }
}
document.addEventListener("click", function (e) {
    var t = (e.target && e.target.closest) ? e.target.closest("[data-rask-gesture]") : null;
    if (!t || !inRoot(t)) { return; }
    var raw = t.getAttribute("data-rask-gesture");
    if (!raw) { return; }
    var spec;
    try { spec = JSON.parse(raw); } catch (err) { return; }
    var run = raskGestureCaps[spec.cap];
    if (!run) { return; }
    // Resolve an optional target element from its ElementRef id (data-rask-ref), same selector the ref reviver uses.
    var el = spec.el ? document.querySelector('[data-rask-ref="' + spec.el + '"]') : undefined;
    var result;
    try { result = run(spec.arg, el); } catch (err) { if (spec.rid != null) { raskPostGestureResult(spec.rid, null); } return; }
    var thenable = result && typeof result.then === "function";
    if (spec.rid != null) {
        // Always post back when a result is expected, so the one-shot server-side handler is consumed
        // (never left dangling) — even if the cap returned a non-thenable (e.g. an unavailable capability).
        if (thenable) {
            result.then(function (value) { raskPostGestureResult(spec.rid, value); },
                function () { raskPostGestureResult(spec.rid, null); });
        } else {
            raskPostGestureResult(spec.rid, result == null ? null : result);
        }
    } else if (thenable && result["catch"]) {
        result["catch"](function () {});
    }
});


// ----- IJSRuntime bridge -----------------------------------------------------
// Called by Rask.Wasm.JSInterop.BeginInvokeJSImport (a [JSImport]). Walks the
// dotted identifier on `window`, invokes it with the JSON-decoded args, then
// ships the result back through the EndInvokeJSResult JSExport — same shape as
// the server-side dispatcher in rask.js.

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
        if (typeof value.__jsObjectId === "number") {
            return jsObjectRefs.get(value.__jsObjectId);
        }
        // ElementRef: {"__raskRef__":"id"} -> the live DOM element (or null if not in the DOM).
        // CSS.escape the id so a value carrying a quote/bracket can't break out of the
        // attribute selector or match an unintended element (defense-in-depth — ids are
        // framework-minted, but the reviver runs on server-supplied JSON).
        if (typeof value.__raskRef__ === "string") {
            return document.querySelector(`[data-rask-ref="${CSS.escape(value.__raskRef__)}"]`);
        }
    }
    return value;
}

function endInvokeJSResult(taskId, success, result, error) {
    if (!dotnetExports || !dotnetExports.Rask || !dotnetExports.Rask.Wasm
        || !dotnetExports.Rask.Wasm.JSInterop) return;
    const payload = success
        ? [Number(taskId), true, (result === undefined ? null : result)]
        : [Number(taskId), false, error || "JS invocation failed"];
    try {
        dotnetExports.Rask.Wasm.JSInterop.EndInvokeJSResult(JSON.stringify(payload));
    } catch (e) {
        console.error("[Rask] EndInvokeJSResult failed", e);
    }
}

export function beginInvokeJS(taskId, identifier, argsJson, resultType, targetInstanceId) {
    // Two gates for Rask.* identifiers:
    //  1. headAssetsReady() — user-Head-declared CDN <script>/<link> deps still loading.
    //  2. raskNamespaceReady() — the component's per-component script
    //     (/_rask/a/{hash}.js, served by the host endpoint) hasn't executed yet, so
    //     window.Rask.{TypeName} doesn't exist. First-render OnRenderedAsync races this
    //     load; the parked invoke wakes up via the polling tick when the script's IIFE
    //     populates window.Rask.{TypeName}.
    if (typeof identifier === "string"
        && identifier.indexOf("Rask.") === 0
        && (!scopedJsReady || !headAssetsReady() || !raskNamespaceReady(identifier))) {
        pendingScopedInvokes.push({taskId, identifier, argsJson, resultType, targetInstanceId});
        ensureRaskNamespacePoll();
        return;
    }
    dispatchUnparked(taskId, identifier, argsJson, resultType, targetInstanceId);
}

function dispatchUnparked(taskId, identifier, argsJson, resultType, targetInstanceId) {
    Promise.resolve().then(() => {
        let args;
        try {
            args = JSON.parse(argsJson || "[]", jsReviver);
        } catch (e) {
            throw new Error("Failed to parse argsJson: " + e.message);
        }

        let target = window;
        const targetId = Number(targetInstanceId);
        if (targetId !== 0) {
            target = jsObjectRefs.get(targetId);
            if (!target) throw new Error("Unknown JS object reference: " + targetInstanceId);
        }

        const resolved = jsResolveIdentifier(target, identifier);
        if (!resolved) throw new Error("Could not find '" + identifier + "' on target");
        const parent = resolved[0];
        const key = resolved[1];
        const fn = parent[key];
        return (typeof fn === "function") ? fn.apply(parent, args) : fn;
    }).then((value) => {
        if (resultType === 3) {
            endInvokeJSResult(taskId, true, null);
            return;
        }
        if (resultType === 1) {
            const refId = nextJsObjectRefId++;
            jsObjectRefs.set(refId, value);
            endInvokeJSResult(taskId, true, {"__jsObjectId": refId});
            return;
        }
        endInvokeJSResult(taskId, true, value);
    }).catch((err) => {
        endInvokeJSResult(taskId, false, null, (err && err.message) || String(err));
    });
}

// ----- DotNet shim (mirror of Blazor's window.DotNet, for [JSInvokable]) -----
const dotNetPending = new Map();
let nextDotNetCallId = 1;

window.DotNet = window.DotNet || {
    invokeMethodAsync(assemblyName, methodIdentifier, ...args) {
        const callId = String(nextDotNetCallId++);
        return new Promise((resolve, reject) => {
            dotNetPending.set(callId, {resolve, reject});
            if (!dotnetExports || !dotnetExports.Rask || !dotnetExports.Rask.Wasm
                || !dotnetExports.Rask.Wasm.JSInterop) {
                dotNetPending.delete(callId);
                reject(new Error("Rask.Wasm.JSInterop not ready"));
                return;
            }
            dotnetExports.Rask.Wasm.JSInterop.BeginDotNetInvoke(
                callId, assemblyName, methodIdentifier, 0, JSON.stringify(args));
        });
    }
};

export function endDotNetInvoke(resultJson) {
    let msg;
    try {
        msg = JSON.parse(resultJson);
    } catch (e) {
        console.error("[Rask] endDotNetInvoke: malformed JSON", e);
        return;
    }
    const pending = dotNetPending.get(msg.callId);
    if (!pending) return;
    dotNetPending.delete(msg.callId);
    if (msg.success) pending.resolve(msg.result);
    else pending.reject(new Error(msg.error || "DotNet invocation failed"));
}
