// The C#-facing adapter over ./ — and the ONLY module in this directory with side effects.
//
// Rask's C# wrappers reach the browser by handing IJSRuntime a dotted identifier ("__raskApi.
// geolocation") that the invoke dispatcher resolves against `window` at call time. That is why these
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

import * as battery from "./battery.js";
import * as broadcastChannel from "./broadcastChannel.js";
import * as cookies from "./cookies.js";
import * as crypto from "./crypto.js";
import * as deviceMotion from "./deviceMotion.js";
import * as deviceOrientation from "./deviceOrientation.js";
import * as eyeDropper from "./eyeDropper.js";
import * as fileSystem from "./fileSystem.js";
import * as fullscreen from "./fullscreen.js";
import * as gamepad from "./gamepad.js";
import * as installPrompt from "./installPrompt.js";
import * as geolocation from "./geolocation.js";
import * as indexedDb from "./indexedDb.js";
import * as intersectionObserver from "./intersectionObserver.js";
import * as mediaDevices from "./mediaDevices.js";
import * as mediaQuery from "./mediaQuery.js";
import * as mediaSession from "./mediaSession.js";
import * as mutationObserver from "./mutationObserver.js";
import * as networkInformation from "./networkInformation.js";
import * as opfs from "./originPrivateFileSystem.js";
import * as performance from "./performance.js";
import * as permissions from "./permissions.js";
import * as pictureInPicture from "./pictureInPicture.js";
import * as resizeObserver from "./resizeObserver.js";
import * as screenInfo from "./screen.js";
import * as screenOrientation from "./screenOrientation.js";
import * as speechRecognition from "./speechRecognition.js";
import * as speechSynthesis from "./speechSynthesis.js";
import * as storageManager from "./storageManager.js";
import * as visualViewport from "./visualViewport.js";
import * as webAuthn from "./webAuthn.js";
import * as webLocks from "./webLocks.js";

window.__raskApi = window.__raskApi || {
    // IGeolocation.GetCurrentPositionAsync. Rejects when unsupported, denied or timed out; the
    // awaiting ValueTask surfaces that as a JSException.
    geolocation: (
        enableHighAccuracy: boolean,
        timeoutMs: number | null,
        maximumAgeMs: number | null) =>
        geolocation.getCurrentPosition({enableHighAccuracy, timeoutMs, maximumAgeMs}),

    // IPermissions.QueryAsync — the live PermissionStatus flattened to its state string.
    permissionState: (name: PermissionName) => permissions.query(name),

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

    // IMediaQuery — just the boolean, since MediaQueryList is live and does not serialize.
    matchMedia: (query: string) => mediaQuery.matches(query),

    // IStorageEstimator.
    storageSupported: () => storageManager.isSupported(),
    storageEstimate: () => storageManager.estimate(),
    storagePersisted: () => storageManager.persisted(),
    storagePersist: () => storageManager.persist(),

    // IVisualViewport.
    visualViewportSupported: () => visualViewport.isSupported(),
    visualViewport: () => visualViewport.current(),

    // IScreenInfo.
    screen: () => screenInfo.info(),

    // ISpeechSynthesis.
    speechSupported: () => speechSynthesis.isSupported(),
    speak: (text: string, options?: RaskSpeakOptions | null) =>
        speechSynthesis.speak(text, options || undefined),
    cancelSpeech: () => speechSynthesis.cancel(),

    // INetworkInfo.
    networkSupported: () => networkInformation.isSupported(),
    network: () => networkInformation.current()
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

// IPerformance.
window.__raskPerf = window.__raskPerf || {
    now: () => performance.now(),
    navigation: () => performance.navigation()
};

// ICrypto. randomBytes crosses as a plain number array — a Uint8Array does not survive the JSON hop
// the Server transport takes, and the module hands back the typed array a TypeScript caller wants.
window.__raskCrypto = window.__raskCrypto || {
    randomUuid: () => crypto.randomUuid(),
    randomBytes: (length: number) => Array.from(crypto.randomBytes(length)),
    digestHex: (algorithm: AlgorithmIdentifier, text: string) => crypto.digestHex(algorithm, text)
};

// IResizeObserver / IIntersectionObserver. Same shape for both: C# mints the id, the element arrives
// already resolved from an ElementRef by the JSON reviver, and the stop function is parked under the
// id. A null element means the ref never resolved — nothing to observe, and nothing to report.
window.__raskResize = window.__raskResize || (() => {
    const stops = new Map<number, () => void>();
    return {
        observe: (id: number, element: Element | null) => {
            if (!element) {
                return;
            }
            stops.set(id, resizeObserver.observe(element, (rect) =>
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskResizeChanged", id, rect)));
        },
        unobserve: (id: number) => {
            const stop = stops.get(id);
            if (!stop) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();

window.__raskIntersect = window.__raskIntersect || (() => {
    const stops = new Map<number, () => void>();
    return {
        observe: (
            id: number,
            element: Element | null,
            thresholds: number[] | null,
            rootMargin: string | null) => {
            if (!element) {
                return;
            }
            stops.set(id, intersectionObserver.observe(
                element,
                (change) =>
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskIntersectionChanged", id, change),
                {thresholds, rootMargin}));
        },
        unobserve: (id: number) => {
            const stop = stops.get(id);
            if (!stop) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();

// IBattery. watch resolves as soon as the subscription is REGISTERED rather than once the manager has
// arrived: navigator.getBattery is a promise, and the module hands back a stop function synchronously
// precisely so a clear that lands mid-flight cannot leave listeners attached with nothing holding
// them.
window.__raskBattery = window.__raskBattery || (() => {
    const stops = new Map<number, () => void>();
    return {
        isSupported: () => battery.isSupported(),
        getStatus: () => battery.getStatus(),
        watch: (id: number) => {
            stops.set(id, battery.watch((status) =>
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskBatteryChanged", id, status)));
            return Promise.resolve();
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

// IOriginPrivateFileSystem. Path-based on both sides, so this is only the base64 hop — and `delete`,
// which C# calls it and TypeScript cannot export under that name.
window.__raskOpfs = window.__raskOpfs || (() => {
    const toBase64 = (bytes: Uint8Array) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };

    const fromBase64 = (base64: string) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };

    return {
        isSupported: () => opfs.isSupported(),
        exists: (path: string) => opfs.exists(path),
        size: (path: string) => opfs.size(path),
        read: async (path: string, offset: number, count: number) => {
            const bytes = await opfs.read(path, offset, count);
            return bytes === null ? null : toBase64(bytes);
        },
        readAll: async (path: string) => {
            const bytes = await opfs.readAll(path);
            return bytes === null ? null : toBase64(bytes);
        },
        write: (path: string, offset: number, base64: string) =>
            opfs.write(path, offset, fromBase64(base64)),
        writeAll: (path: string, base64: string) => opfs.writeAll(path, fromBase64(base64)),
        truncate: (path: string, size: number) => opfs.truncate(path, size),
        delete: (path: string, recursive: boolean) => opfs.remove(path, recursive),
        list: (path: string) => opfs.list(path)
    };
})();

// IFileSystemAccess. A FileSystemHandle cannot cross interop, so handles are held here under an id and
// C# operates by id — the module hands back the handle itself, which is what a TypeScript caller wants
// to keep. Bytes cross base64-encoded for the same reason as IndexedDB's.
window.__raskFs = window.__raskFs || (() => {
    const handles = new Map<number, FileSystemHandle>();
    let nextId = 0;

    const put = (handle: FileSystemHandle) => {
        const id = ++nextId;
        handles.set(id, handle);
        return {id, name: handle.name};
    };

    const fileOf = (id: number): FileSystemFileHandle => {
        const h = handles.get(id);
        if (!h || h.kind !== "file") {
            throw new Error("Rask file system: file handle " + id + " is closed.");
        }
        return h as FileSystemFileHandle;
    };

    const dirOf = (id: number): FileSystemDirectoryHandle => {
        const h = handles.get(id);
        if (!h || h.kind !== "directory") {
            throw new Error("Rask file system: directory handle " + id + " is closed.");
        }
        return h as FileSystemDirectoryHandle;
    };

    const toBase64 = (bytes: Uint8Array) => {
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };

    const fromBase64 = (base64: string) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };

    return {
        isSupported: () => fileSystem.isSupported(),
        openFile: async (opts: RaskFilePickerOptions | null) => {
            const handle = await fileSystem.openFile(opts);
            return handle ? put(handle) : null;
        },
        openFiles: async (opts: RaskFilePickerOptions | null) =>
            (await fileSystem.openFiles(opts)).map(put),
        saveFile: async (opts: RaskFilePickerOptions | null) => {
            const handle = await fileSystem.saveFile(opts);
            return handle ? put(handle) : null;
        },
        openDirectory: async () => {
            const handle = await fileSystem.openDirectory();
            return handle ? put(handle) : null;
        },
        readText: (id: number) => fileSystem.readText(fileOf(id)),
        readBytes: async (id: number) => toBase64(await fileSystem.readBytes(fileOf(id))),
        writeText: (id: number, text: string) => fileSystem.writeText(fileOf(id), text),
        writeBytes: (id: number, base64: string) =>
            fileSystem.writeBytes(fileOf(id), fromBase64(base64)),
        list: (id: number) => fileSystem.list(dirOf(id)),
        getFile: async (id: number, name: string, create: boolean) =>
            put(await fileSystem.getFile(dirOf(id), name, create)),
        release: (id: number) => {
            handles.delete(id);
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
installPrompt.listen();

window.__raskInstall = window.__raskInstall || {
    canInstall: () => installPrompt.canInstall(),
    isInstalled: () => installPrompt.isInstalled(),
    prompt: () => installPrompt.prompt()
};

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

// IMediaSession. The browser holds one handler per action, while C# hands out a disposable per
// registration — so the id that currently OWNS each action is tracked here. Without that, disposing an
// older registration would clear a handler a newer one had since installed.
window.__raskMediaSession = window.__raskMediaSession || (() => {
    const actions = new Map<number, MediaSessionAction>();
    const owners = new Map<MediaSessionAction, number>();
    return {
        isSupported: () => mediaSession.isSupported(),
        setMetadata: (m: RaskMediaMetadata) => mediaSession.setMetadata(m),
        setPlaybackState: (state: MediaSessionPlaybackState) => mediaSession.setPlaybackState(state),
        setActionHandler: (id: number, action: MediaSessionAction) => {
            mediaSession.setActionHandler(action, () =>
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskMediaSessionAction", id));
            actions.set(id, action);
            owners.set(action, id);
        },
        removeActionHandler: (id: number) => {
            const action = actions.get(id);
            if (action === undefined) {
                return;
            }
            actions.delete(id);
            if (owners.get(action) === id) {
                owners.delete(action);
                mediaSession.setActionHandler(action, null);
            }
        },
        clear: () => mediaSession.clear()
    };
})();

// IMutationObserver. The seven positional flags are what an IJSRuntime call site can express; the
// module takes them as one options object.
window.__raskMutation = window.__raskMutation || (() => {
    const stops = new Map<number, () => void>();
    return {
        observe: (
            id: number,
            element: Element | null,
            childList: boolean,
            attributes: boolean,
            characterData: boolean,
            subtree: boolean,
            attributeFilter: string[] | null) => {
            if (!element) {
                return;
            }
            stops.set(id, mutationObserver.observe(
                element,
                (change) =>
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskMutationChanged", id, change),
                {childList, attributes, characterData, subtree, attributeFilter}));
        },
        unobserve: (id: number) => {
            const stop = stops.get(id);
            if (!stop) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();

// IBroadcastChannel. C# holds an integer id rather than the channel object, which cannot cross.
window.__raskBroadcast = window.__raskBroadcast || (() => {
    const channels = new Map<number, broadcastChannel.Channel>();
    return {
        open: (id: number, name: string) => {
            channels.set(id, broadcastChannel.open(name, (message) =>
                window.DotNet.invokeMethodAsync("Rask.Core", "RaskBroadcastReceive", id, message)));
        },
        post: (id: number, message: string) => {
            const channel = channels.get(id);
            if (channel) {
                channel.post(message);
            }
        },
        close: (id: number) => {
            const channel = channels.get(id);
            if (!channel) {
                return;
            }
            channels.delete(id);
            channel.close();
        }
    };
})();

// IGamepad. Polled at ~12 Hz rather than every animation frame, for the same reason the sensors are
// throttled: each reading that changes is a frame on the wire. In-page callers poll every frame.
const GAMEPAD_POLL_MS = 80;

window.__raskGamepad = window.__raskGamepad || (() => {
    const stops = new Map<number, () => void>();
    return {
        isSupported: () => gamepad.isSupported(),
        watch: (id: number) => {
            stops.set(id, gamepad.watch(
                (reading) =>
                    window.DotNet.invokeMethodAsync("Rask.Core", "RaskGamepadReading", id, reading),
                {throttleMs: GAMEPAD_POLL_MS}));
        },
        unwatch: (id: number) => {
            const stop = stops.get(id);
            if (!stop) {
                return;
            }
            stops.delete(id);
            stop();
        }
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

// IGeolocation.WatchAsync. C# mints the id and holds the subscription, so the stop function the
// module returns is parked here under that id rather than handed back.
window.__raskGeoWatch = window.__raskGeoWatch || (() => {
    const stops = new Map<number, () => void>();
    return {
        watch: (
            id: number,
            enableHighAccuracy: boolean,
            timeoutMs: number | null,
            maximumAgeMs: number | null) => {
            const stop = geolocation.watchPosition(
                (fix) => window.DotNet.invokeMethodAsync("Rask.Core", "RaskGeolocationFix", id, fix),
                {enableHighAccuracy, timeoutMs, maximumAgeMs});
            stops.set(id, stop);
        },
        clear: (id: number) => {
            const stop = stops.get(id);
            if (stop == null) {
                return;
            }
            stops.delete(id);
            stop();
        }
    };
})();
