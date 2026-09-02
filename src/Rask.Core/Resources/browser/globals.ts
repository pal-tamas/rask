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
import * as cookies from "./cookies.js";
import * as crypto from "./crypto.js";
import * as deviceMotion from "./deviceMotion.js";
import * as deviceOrientation from "./deviceOrientation.js";
import * as geolocation from "./geolocation.js";
import * as indexedDb from "./indexedDb.js";
import * as intersectionObserver from "./intersectionObserver.js";
import * as mediaQuery from "./mediaQuery.js";
import * as networkInformation from "./networkInformation.js";
import * as performance from "./performance.js";
import * as permissions from "./permissions.js";
import * as resizeObserver from "./resizeObserver.js";
import * as screenInfo from "./screen.js";
import * as speechRecognition from "./speechRecognition.js";
import * as speechSynthesis from "./speechSynthesis.js";
import * as storageManager from "./storageManager.js";
import * as visualViewport from "./visualViewport.js";

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
