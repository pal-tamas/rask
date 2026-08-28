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
        if (m.theme_color) {
            let meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
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
            window.IdleDetector ? IdleDetector!.requestPermission().catch(() => "denied") : Promise.resolve("denied"),
        watch: async (id: number, thresholdSeconds: number) => {
            const controller = new AbortController();
            // Reached only through isSupported(); the constructor is optional in the declaration
            // because the API is Chromium-only.
            const detector = new IdleDetector!();
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
        unwatch: (id: number) => {
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
    const toB64 = (bytes: Uint8Array) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64: string) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const read = (id: number, port: SerialPortLike) => {
        const entry = ports.get(id);
        if (!entry || !port.readable) {
            return;
        }
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
            } catch {
                // Device unplugged or stream error — fall through to teardown below.
            } finally {
                try { reader.releaseLock(); } catch { /* already released */ }
            }
            // Natural end (not an explicit close): release the port and notify C# so it can fire onClosed.
            if (!entry.closing) {
                ports.delete(id);
                try { await port.close(); } catch { /* already gone */ }
                window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskSerialClosed", id);
            }
        })();
    };
    return {
        isSupported: () => "serial" in navigator,
        requestPort: async (id: number, o: RaskSerialOptions) => {
            let port: SerialPortLike;
            try {
                // Drop null/absent ids so a vendor-only filter doesn't coerce to productId 0 (empty chooser).
                const filters = (o.filters || [])
                    .map((f) => {
                        const x: { usbVendorId?: number; usbProductId?: number } = {};
                        if (f.usbVendorId != null) { x.usbVendorId = f.usbVendorId; }
                        if (f.usbProductId != null) { x.usbProductId = f.usbProductId; }
                        return x;
                    })
                    .filter((x) => Object.keys(x).length > 0);
                port = await navigator.serial!.requestPort(filters.length ? {filters: filters} : {});
            } catch {
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
        write: (id: number, data: string) => {
            const entry = ports.get(id);
            if (!entry) {
                return Promise.resolve();
            }
            // data is base64 (raw byte[] doesn't marshal); serialize writes so concurrent sends don't collide
            // on the single writable-stream lock.
            const bytes = fromB64(data);
            entry.writeChain = entry.writeChain.then(async () => {
                const writable = entry.port.writable;
                if (!writable) {
                    return;
                }
                const writer = writable.getWriter();
                try {
                    await writer.write(bytes);
                } finally {
                    writer.releaseLock();
                }
            });
            return entry.writeChain;
        },
        close: async (id: number) => {
            const entry = ports.get(id);
            if (!entry) {
                return;
            }
            entry.closing = true; // tell the read loop this end is deliberate (skip the teardown/notify path)
            ports.delete(id);
            if (entry.reader) {
                try { await entry.reader.cancel(); } catch { /* already cancelled */ }
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
    const toB64 = (bytes: Uint8Array) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64: string) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const dataB64 = (view: DataView | undefined) =>
        view ? toB64(new Uint8Array(view.buffer, view.byteOffset, view.byteLength)) : "";
    const info = (d: USBDeviceLike) => ({
        vendorId: d.vendorId,
        productId: d.productId,
        manufacturerName: d.manufacturerName || null,
        productName: d.productName || null,
        serialNumber: d.serialNumber || null
    });
    // The browser hands back the same USBDevice object from requestDevice/getDevices, so dedup to one id and
    // refcount it — a handle's close() only tears the device down once every C# handle to it has closed.
    const put = (device: USBDeviceLike) => {
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
    const evict = (id: number) => {
        const device = byId.get(id);
        if (device) {
            byId.delete(id);
            idByDevice.delete(device);
        }
        refs.delete(id);
    };
    // A stale/closed id throws a clear error rather than an opaque "reading 'open' of undefined" TypeError.
    const dev = (id: number) => {
        const device = byId.get(id);
        if (!device) {
            throw new Error("USB device handle is closed or unknown (id " + id + ")");
        }
        return device;
    };
    if (navigator.usb) {
        navigator.usb.addEventListener("disconnect", (e: Event) => {
            const id = idByDevice.get((e as USBConnectionEventLike).device);
            if (id !== undefined) {
                evict(id);
                window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskUsbDisconnected", id);
            }
        });
    }
    return {
        isSupported: () => "usb" in navigator,
        requestDevice: async (filters: unknown[] | null) => {
            let device;
            try {
                device = await navigator.usb!.requestDevice({filters: filters || []});
            } catch (e) {
                if (e instanceof Error && e.name === "NotFoundError") {
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
            return (await navigator.usb!.getDevices()).map((d) => put(d));
        },
        open: (id: number) => dev(id).open(),
        selectConfiguration: (id: number, configurationValue: number) =>
            dev(id).selectConfiguration(configurationValue),
        claimInterface: (id: number, interfaceNumber: number) => dev(id).claimInterface(interfaceNumber),
        releaseInterface: (id: number, interfaceNumber: number) => dev(id).releaseInterface(interfaceNumber),
        transferIn: async (id: number, endpointNumber: number, length: number) => {
            const r = await dev(id).transferIn(endpointNumber, length);
            return {status: r.status, data: dataB64(r.data)};
        },
        transferOut: async (id: number, endpointNumber: number, base64: string) => {
            const r = await dev(id).transferOut(endpointNumber, fromB64(base64));
            return {status: r.status, bytesWritten: r.bytesWritten};
        },
        controlTransferIn: async (id: number, setup: unknown, length: number) => {
            const r = await dev(id).controlTransferIn(setup, length);
            return {status: r.status, data: dataB64(r.data)};
        },
        controlTransferOut: async (id: number, setup: unknown, base64: string) => {
            const r = await dev(id).controlTransferOut(setup, fromB64(base64));
            return {status: r.status, bytesWritten: r.bytesWritten};
        },
        close: async (id: number) => {
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
    const toB64 = (bytes: Uint8Array) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64: string) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const dataB64 = (view: DataView | undefined) =>
        view ? toB64(new Uint8Array(view.buffer, view.byteOffset, view.byteLength)) : "";
    const info = (d: HIDDevice) => ({vendorId: d.vendorId, productId: d.productId, productName: d.productName || null});
    // The browser hands back the same HIDDevice object from requestDevice/getDevices, so dedup to one id and
    // refcount it — a handle's close() only tears the device down once every C# handle to it has closed.
    const put = (device: HIDDevice) => {
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
    const detach = (id: number) => {
        const device = byId.get(id);
        const listener = listeners.get(id);
        if (device && listener) {
            try { device.removeEventListener("inputreport", listener); } catch (e) { /* gone */ }
        }
        listeners.delete(id);
        watchCounts.delete(id);
    };
    const evict = (id: number) => {
        detach(id);
        const device = byId.get(id);
        if (device) {
            byId.delete(id);
            idByDevice.delete(device);
        }
        refs.delete(id);
    };
    // A stale/closed id throws a clear error rather than an opaque "reading 'open' of undefined" TypeError.
    const dev = (id: number) => {
        const device = byId.get(id);
        if (!device) {
            throw new Error("HID device handle is closed or unknown (id " + id + ")");
        }
        return device;
    };
    if (navigator.hid) {
        navigator.hid.addEventListener("disconnect", (e: Event) => {
            const id = idByDevice.get((e as HIDConnectionEvent).device);
            if (id !== undefined) {
                evict(id);
                window.DotNet.invokeMethodAsync("Rask.Wasm", "RaskHidDisconnected", id);
            }
        });
    }
    return {
        isSupported: () => "hid" in navigator,
        // navigator.hid.requestDevice resolves with [] on cancel (no rejection); real errors propagate.
        requestDevices: async (filters: HIDDeviceFilter[] | null) => {
            if (!("hid" in navigator)) {
                return [];
            }
            return (await navigator.hid!.requestDevice({filters: filters || []})).map((d: HIDDevice) => put(d));
        },
        getDevices: async () => {
            if (!("hid" in navigator)) {
                return [];
            }
            return (await navigator.hid!.getDevices()).map((d: HIDDevice) => put(d));
        },
        open: (id: number) => dev(id).open(),
        close: async (id: number) => {
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
        sendReport: (id: number, reportId: number, base64: string) =>
            dev(id).sendReport(reportId, fromB64(base64)),
        sendFeatureReport: (id: number, reportId: number, base64: string) =>
            dev(id).sendFeatureReport(reportId, fromB64(base64)),
        receiveFeatureReport: async (id: number, reportId: number) =>
            dataB64(await dev(id).receiveFeatureReport(reportId)),
        watch: (id: number) => {
            const device = dev(id);
            const count = watchCounts.get(id) || 0;
            if (count === 0) {
                const listener = (e: HIDInputReportEvent) => {
                    window.DotNet.invokeMethodAsync(
                        "Rask.Wasm", "RaskHidInputReport", id, e.reportId, dataB64(e.data));
                };
                listeners.set(id, listener);
                device.addEventListener("inputreport", listener);
            }
            watchCounts.set(id, count + 1);
        },
        unwatch: (id: number) => {
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
    const toB64 = (bytes: Uint8Array) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };
    const fromB64 = (base64: string) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };
    const dataB64 = (view: DataView | undefined) =>
        view ? toB64(new Uint8Array(view.buffer, view.byteOffset, view.byteLength)) : "";
    const info = (d: BluetoothDeviceLike) => ({id: d.id, name: d.name || null});
    const putDevice = (device: BluetoothDeviceLike) => {
        let id = idByDevice.get(device);
        if (id === undefined) {
            id = ++nextDeviceId;
            byId.set(id, device);
            idByDevice.set(device, id);
        }
        return {id: id, info: info(device)};
    };
    const dev = (id: number) => {
        const device = byId.get(id);
        if (!device) {
            throw new Error("Bluetooth device handle is closed or unknown (id " + id + ")");
        }
        return device;
    };
    const ch = (id: number) => {
        const characteristic = chars.get(id);
        if (!characteristic) {
            throw new Error("Bluetooth characteristic handle is unknown (id " + id + ")");
        }
        return characteristic;
    };
    const detachDisconnect = (deviceId: number) => {
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
        requestDevice: async (o: RaskBluetoothOptions) => {
            const opts: RaskBluetoothRequest = o.acceptAllDevices
                ? {acceptAllDevices: true}
                : {filters: o.filters || []};
            if (o.optionalServices && o.optionalServices.length) {
                opts.optionalServices = o.optionalServices;
            }
            let device;
            try {
                device = await navigator.bluetooth!.requestDevice(opts);
            } catch (e) {
                if (e instanceof Error && e.name === "NotFoundError") {
                    return null; // user dismissed the chooser
                }
                throw e;
            }
            return putDevice(device);
        },
        getDevices: async () => {
            if (!navigator.bluetooth || !navigator.bluetooth!.getDevices) {
                return [];
            }
            return (await navigator.bluetooth!.getDevices()).map((d) => putDevice(d));
        },
        connect: async (id: number) => { await dev(id).gatt!.connect(); },
        // Reusable: drops the GATT link but keeps the handle (reconnect with connect()). release() evicts.
        disconnect: (id: number) => {
            const device = byId.get(id);
            if (device) {
                try { device.gatt.disconnect(); } catch (e) { /* already disconnected */ }
            }
        },
        release: (id: number) => {
            const device = byId.get(id);
            if (!device) {
                return;
            }
            detachDisconnect(id);
            byId.delete(id);
            idByDevice.delete(device);
            try { device.gatt.disconnect(); } catch (e) { /* already disconnected */ }
        },
        isConnected: (id: number) => !!dev(id).gatt?.connected,
        getCharacteristic: async (id: number, serviceUuid: string, characteristicUuid: string) => {
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
        readValue: async (charId: number) => dataB64(await ch(charId).readValue()),
        writeValue: async (charId: number, base64: string, withResponse: boolean) => {
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
        startNotifications: async (charId: number) => {
            const characteristic = ch(charId);
            const count = notifyCounts.get(charId) || 0;
            if (count === 0) {
                const listener = (e: Event) => {
                    const target = e.target as BluetoothCharacteristicLike;
                    window.DotNet.invokeMethodAsync(
                        "Rask.Wasm", "RaskBluetoothValue", charId, dataB64(target.value));
                };
                valueListeners.set(charId, listener);
                characteristic.addEventListener("characteristicvaluechanged", listener);
                await characteristic.startNotifications();
            }
            notifyCounts.set(charId, count + 1);
        },
        stopNotifications: async (charId: number) => {
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
        releaseCharacteristic: (charId: number) => {
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
        watchDisconnect: (deviceId: number) => {
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
        unwatchDisconnect: (deviceId: number) => {
            const remaining = (discCounts.get(deviceId) || 0) - 1;
            if (remaining > 0) {
                discCounts.set(deviceId, remaining);
                return;
            }
            detachDisconnect(deviceId);
        }
    };
})();

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
