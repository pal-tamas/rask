// The C#-facing adapter over ./ — and the ONLY module in this directory with side effects.
//
// Rask's C# wrappers reach the browser by handing IJSRuntime a dotted identifier ("__raskApi.
// cookieGet") that the invoke dispatcher resolves against `window` at call time. That is why these
// are globals rather than exports: the caller is .NET, and it resolves names, not modules.
//
// Importing this file registers those namespaces. Both framework clients do exactly that — Server's
// rask.ts and WASM's rask.wasm.ts — while a TypeScript front end imports the modules beside it and
// never loads this file at all.
//
// What lives HERE rather than in a module is everything that belongs to .NET's calling convention
// and not to the browser:
//
//   * positional arguments, because an IJSRuntime call site has no object literals to spare;
//   * numeric ids and the maps that key subscriptions by them, because C# owns the id and a
//     `() => void` cannot cross the interop boundary;
//   * DotNet.invokeMethodAsync callbacks into [JSInvokable] statics.
//
// The keys and signatures below are a contract with the C# wrappers. Renaming one is a silent
// break — the identifier simply fails to resolve at run time, in the browser, with no compiler
// anywhere in the path to notice.

import * as cookies from "./cookies.js";
import * as deviceMotion from "./deviceMotion.js";
import * as deviceOrientation from "./deviceOrientation.js";
import * as eyeDropper from "./eyeDropper.js";
import * as fullscreen from "./fullscreen.js";
import * as installPrompt from "./installPrompt.js";
import * as indexedDb from "./indexedDb.js";
import * as mediaDevices from "./mediaDevices.js";
import * as pictureInPicture from "./pictureInPicture.js";
import * as screenOrientation from "./screenOrientation.js";
import * as signaling from "./signaling.js";
import * as speechRecognition from "./speechRecognition.js";
import * as storageManager from "./storageManager.js";
import * as wakeLock from "./wakeLock.js";
import * as webAuthn from "./webAuthn.js";
import * as webLocks from "./webLocks.js";
import * as webPush from "./webPush.js";

window.__raskApi = window.__raskApi || {
    // ICookies. Positional here, an options object in the module.
    cookieGet: (name: string) => cookies.get(name),
    cookieAll: () => cookies.getAll(),
    cookieSet: (
        name: string,
        value: string,
        maxAge: number | null,
        expires: string | null,
        path: string | null,
        domain: string | null,
        sameSite: string | null,
        secure: boolean) =>
        cookies.set(name, value, {
            maxAgeSeconds: maxAge,
            expires,
            path,
            domain,
            sameSite: sameSite as "Strict" | "Lax" | "None" | null,
            secure
        }),
    cookieDelete: (name: string, path: string | null) => cookies.remove(name, path),

    // IStorageEstimator.
    storageSupported: () => storageManager.isSupported(),
    storageEstimate: () => storageManager.estimate(),
    storagePersisted: () => storageManager.persisted(),
    storagePersist: () => storageManager.persist()
};

// IIndexedDb / IKeyValueStore. C# addresses a store by name on every call rather than holding a
// handle, so the handles are cached here — reopening per call would pay the `upgradeneeded` round
// trip each time.
//
// Bytes cross as base64 because that is the one encoding both interop transports marshal identically.
// The conversion belongs here and not in the module: a TypeScript caller has Uint8Array and should
// keep it, and storing base64 text in the object store would spend about a third of the origin's
// quota on encoding.
window.__raskIdb = window.__raskIdb || (() => {
    const stores = new Map<string, Promise<indexedDb.KeyValueStore>>();

    const store = (name: string): Promise<indexedDb.KeyValueStore> => {
        const cached = stores.get(name);
        if (cached) {
            return cached;
        }
        const opened = indexedDb.openStore(name);
        stores.set(name, opened);
        return opened;
    };

    const toBytes = (base64: string): Uint8Array => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    };

    const toBase64 = (value: Uint8Array | ArrayBuffer): string => {
        const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
        // Chunked: String.fromCharCode.apply throws RangeError once the argument list gets long,
        // which for a database-sized value is not a hypothetical.
        const CHUNK = 0x8000;
        let binary = "";
        for (let i = 0; i < bytes.length; i += CHUNK) {
            binary += String.fromCharCode.apply(null, Array.from(bytes.subarray(i, i + CHUNK)));
        }
        return btoa(binary);
    };

    return {
        isSupported: () => indexedDb.isSupported(),
        open: (name: string) => store(name).then(() => undefined),
        set: (name: string, key: string, value: unknown) => store(name).then((s) => s.set(key, value)),
        get: (name: string, key: string) => store(name).then((s) => s.get(key)),
        setBytes: (name: string, key: string, base64: string) =>
            store(name).then((s) => s.setBytes(key, toBytes(base64))),
        getBytes: (name: string, key: string) =>
            store(name).then((s) => s.getBytes(key)).then((v) => (v === null ? null : toBase64(v))),
        delete: (name: string, key: string) => store(name).then((s) => s.remove(key)),
        keys: (name: string) => store(name).then((s) => s.keys()),
        clear: (name: string) => store(name).then((s) => s.clear())
    };
})();

// The PWA pair — IWebPush, IWakeLock. These used to live in rask-pwa.ts, which
// is now gone: they are transport-agnostic browser APIs like the rest, and there was no reason for
// them to sit in a second file with its own import in both entry points.
window.__raskPush = window.__raskPush || {
    isSupported: () => webPush.isSupported(),
    requestPermission: () => webPush.requestPermission(),
    register: (swUrl: string) => webPush.register(swUrl),
    subscribe: (vapidPublicKey: string) => webPush.subscribe(vapidPublicKey),
    getSubscription: () => webPush.getSubscription(),
    unsubscribe: () => webPush.unsubscribe()
};

// IWakeLock. C# holds an integer id where the module hands back a handle; the re-acquire-on-visible
// behaviour that makes a lock survive the user glancing at another tab lives in the module, because it
// is the API's real behaviour rather than anything to do with interop.
window.__raskWakeLock = window.__raskWakeLock || (() => {
    const held = new Map<number, wakeLock.WakeLockHandle>();
    let nextId = 1;
    return {
        isSupported: () => wakeLock.isSupported(),
        request: async () => {
            const handle = await wakeLock.request();
            const id = nextId++;
            held.set(id, handle);
            return id;
        },
        release: async (id: number) => {
            const handle = held.get(id);
            if (!handle) {
                return;
            }
            held.delete(id);
            await handle.release();
        }
    };
})();

// ISignaling. The connection object cannot cross interop, so C# holds an integer id for it. Both
// callbacks are flattened into one [JSInvokable] because an IJSRuntime call site is cheaper than a
// second one, not because the relay speaks that way.
window.__raskSignal = window.__raskSignal || (() => {
    const conns = new Map<number, signaling.SignalingConnection>();

    const invoke = (method: string, ...args: unknown[]) =>
        window.DotNet.invokeMethodAsync("Rask.Core", method, ...args);

    return {
        isSupported: () => signaling.isSupported(),
        open: async (id: number, path: string) => {
            // `closed` guards the gap between the socket opening and this map assignment: a relay that
            // drops the connection in that window would otherwise delete an entry that is not there
            // yet, and the dead one would be stored a moment later and never removed.
            let closed = false;
            const conn = await signaling.open(path, {
                onMessage: (m) => invoke("RaskSignalMessage", id, m.type, m.peer, m.payload),
                onClose: () => {
                    closed = true;
                    conns.delete(id);
                    invoke("RaskSignalClosed", id);
                }
            });
            if (!closed) {
                conns.set(id, conn);
            }
            return true;
        },
        send: (id: number, json: string) => {
            const conn = conns.get(id);
            if (!conn) {
                throw new Error("Rask signaling: connection " + id + " is closed.");
            }
            conn.send(json);
        },
        close: (id: number) => {
            const conn = conns.get(id);
            if (!conn) {
                return;
            }
            conns.delete(id);
            conn.close();
        }
    };
})();

// IWebAuthn. Almost a pass-through: the module already speaks base64url in both directions, because
// that is what a relying party speaks, not merely what interop needs.
window.__raskWebAuthn = window.__raskWebAuthn || {
    isSupported: () => webAuthn.isSupported(),
    platformAuthenticatorAvailable: () => webAuthn.isPlatformAuthenticatorAvailable(),
    create: (o: RaskWebAuthnCreateOptions) => webAuthn.create(o),
    get: (o: RaskWebAuthnGetOptions) => webAuthn.get(o)
};

// The activation-gated four, plus the install prompt. On the WASM host these back imperative services;
// on Server they back declarative gesture components, which run the call inside the click's own stack
// because a WebSocket round trip loses the transient activation these need.
window.__raskFullscreen = window.__raskFullscreen || {
    isSupported: () => fullscreen.isSupported(),
    isActive: () => fullscreen.isActive(),
    request: (el) => fullscreen.request(el),
    exit: () => fullscreen.exit()
};

window.__raskEyeDropper = window.__raskEyeDropper || {
    isSupported: () => eyeDropper.isSupported(),
    open: () => eyeDropper.open()
};

window.__raskOrientation = window.__raskOrientation || {
    isSupported: () => screenOrientation.isSupported(),
    get: () => screenOrientation.current(),
    lock: (type: OrientationLockType) => screenOrientation.lock(type),
    unlock: () => screenOrientation.unlock()
};

window.__raskPip = window.__raskPip || {
    isSupported: () => pictureInPicture.isSupported(),
    isActive: () => pictureInPicture.isActive(),
    request: (el: HTMLVideoElement | null) =>
        el ? pictureInPicture.request(el) : Promise.reject(new Error("no video element")),
    exit: () => pictureInPicture.exit()
};

// IInstallPrompt. listen() runs at registration rather than at module import, which is what keeps the
// module itself side-effect free — the browser fires beforeinstallprompt once, early, so something has
// to be listening before the app's own code runs.
//
// INSIDE the `||` guard, where the old rask-api.ts attached these listeners too. Outside it, a page
// that evaluates this bundle twice — a front end importing it alongside the framework's own — attaches
// them twice, which the window guard is there to prevent.
window.__raskInstall = window.__raskInstall || (() => {
    installPrompt.listen();
    return {
        canInstall: () => installPrompt.canInstall(),
        isInstalled: () => installPrompt.isInstalled(),
        prompt: () => installPrompt.prompt()
    };
})();

// IMediaDevices. A MediaStream cannot cross interop, so streams are held here under a JS-minted id.
// `get` and `adopt` are not part of the C# surface: they are how other framework helpers — __raskRtc
// sending a captured stream to a peer, or registering a peer's remote stream — trade in the same ids.
window.__raskMedia = window.__raskMedia || (() => {
    const streams = new Map<number, MediaStream>();
    let nextId = 0;

    const put = (stream: MediaStream) => {
        const id = ++nextId;
        streams.set(id, stream);
        return id;
    };

    return {
        isSupported: () => mediaDevices.isSupported(),
        enumerate: () => mediaDevices.enumerate(),
        getUserMedia: async (c: RaskMediaConstraints) =>
            put(await mediaDevices.getUserMedia(c)),
        getDisplayMedia: async () => put(await mediaDevices.getDisplayMedia()),
        attach: (id: number, video: HTMLVideoElement | null) => {
            const stream = streams.get(id);
            if (!stream || !video) {
                return Promise.resolve();
            }
            return mediaDevices.attach(video, stream);
        },
        stop: (id: number) => {
            const stream = streams.get(id);
            if (!stream) {
                return;
            }
            streams.delete(id);
            mediaDevices.stop(stream);
        },
        get: (id: number) => streams.get(id),
        adopt: (stream: MediaStream) => put(stream)
    };
})();

// IWebLocks. The platform holds a lock for as long as the callback's promise is pending, and C# wants
// to do its work in C# — so the callback parks on a promise this resolves when release(id) arrives.
// Nothing of that shape belongs in the module, where `work` is an ordinary async function.
window.__raskLocks = window.__raskLocks || (() => {
    const releasers = new Map<number, () => void>();
    return {
        isSupported: () => webLocks.isSupported(),
        request: (id: number, name: string, mode: LockMode, ifAvailable: boolean) =>
            new Promise<boolean>((granted, failed) => {
                webLocks.request(
                    name,
                    () => {
                        granted(true);
                        return new Promise<void>((release) => releasers.set(id, release));
                    },
                    {mode: mode || "exclusive", ifAvailable})
                    .then((result) => {
                        // null means ifAvailable could not grant it — the callback never ran, so
                        // nothing resolved `granted` yet.
                        if (result === null) {
                            granted(false);
                        }
                    })
                    .catch((e) => {
                        releasers.delete(id);
                        failed(e);
                    });
            }),
        release: (id: number) => {
            const release = releasers.get(id);
            if (release) {
                releasers.delete(id);
                release();
            }
        },
        query: () => webLocks.query()
    };
})();

// ISpeechRecognition. The recognizer's options arrive as one object already, so this is close to a
// pass-through; what it adds is the id-keyed stop.
window.__raskSpeechRecognition = window.__raskSpeechRecognition || (() => {
    const stops = new Map<number, () => void>();
    return {
        isSupported: () => speechRecognition.isSupported(),
        start: (id: number, options: RaskSpeechOptions) => {
            stops.set(id, speechRecognition.start(
                (result) => window.DotNet.invokeMethodAsync("Rask.Core", "RaskSpeechResult", id, result),
                options));
        },
        stop: (id: number) => {
            const stop = stops.get(id);
            if (!stop) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();

// IDeviceOrientation / IDeviceMotion.
//
// The throttle is applied HERE rather than in the modules, because it is a property of this BOUNDARY
// and not of the sensor: these fire at roughly 60 Hz, and every reading that crosses is a WebSocket
// frame on the Server transport with a re-render behind it. A TypeScript front end calling the module
// directly has no wire to protect, and gets every event unless it asks for otherwise.
const SENSOR_THROTTLE_MS = 100;

window.__raskDeviceOrientation = window.__raskDeviceOrientation || (() => {
    const stops = new Map<number, () => void>();
    return {
        isSupported: () => deviceOrientation.isSupported(),
        requestPermission: () => deviceOrientation.requestPermission(),
        watch: (id: number) => {
            stops.set(id, deviceOrientation.watch(
                (reading) =>
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskDeviceOrientation", id, reading),
                {throttleMs: SENSOR_THROTTLE_MS}));
        },
        clear: (id: number) => {
            const stop = stops.get(id);
            if (!stop) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();

window.__raskDeviceMotion = window.__raskDeviceMotion || (() => {
    const stops = new Map<number, () => void>();
    return {
        isSupported: () => deviceMotion.isSupported(),
        requestPermission: () => deviceMotion.requestPermission(),
        watch: (id: number) => {
            stops.set(id, deviceMotion.watch(
                (reading) =>
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskDeviceMotion", id, reading),
                {throttleMs: SENSOR_THROTTLE_MS}));
        },
        clear: (id: number) => {
            const stop = stops.get(id);
            if (!stop) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();

