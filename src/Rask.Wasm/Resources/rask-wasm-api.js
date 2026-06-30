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

// Media Capture / getUserMedia (driven by IMediaDevices). getUserMedia needs transient activation + a
// secure context, so this is WASM-only. The live MediaStream can't cross interop, so each is held here
// under a C#-minted id; the video element is resolved from an ElementRef by the JSON reviver. Stopping a
// stream stops every track, which releases the camera/mic (and turns off the hardware indicator).
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
    const put = (device) => {
        let id = idByDevice.get(device);
        if (id === undefined) {
            id = ++nextId;
            byId.set(id, device);
            idByDevice.set(device, id);
        }
        return {id: id, info: info(device)};
    };
    const evict = (id) => {
        const device = byId.get(id);
        if (device) {
            byId.delete(id);
            idByDevice.delete(device);
        }
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
