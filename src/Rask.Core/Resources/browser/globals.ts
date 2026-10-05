// The C#-facing adapter over ./ — and the ONLY module in this directory with side effects.
//
// Rask's C# wrappers reach the browser by handing IJSRuntime a dotted identifier ("__raskIdb.
// isSupported") that the invoke dispatcher resolves against `window` at call time. That is why these
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

import * as eyeDropper from "./eyeDropper.js";
import * as fullscreen from "./fullscreen.js";
import * as indexedDb from "./indexedDb.js";
import * as mediaDevices from "./mediaDevices.js";
import * as pictureInPicture from "./pictureInPicture.js";
import * as screenOrientation from "./screenOrientation.js";
import * as signaling from "./signaling.js";
import * as webAuthn from "./webAuthn.js";

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

// The activation-gated four. On the WASM host these back imperative services;
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

// Trigger.MediaCapture and IWebRtc. A MediaStream reaches C# as a handle (IJSObjectReference) it takes by id: the
// gesture bridge and __raskRtc's ontrack post an id, since a DotNet.invokeMethodAsync result is data, and C# takes the
// stream it names once, as `__raskMedia.take`, which hands the handle over and forgets the id.
window.__raskMedia = window.__raskMedia || (() => {
    const handed = new Map<number, MediaStream>();
    let nextId = 0;

    const hand = (stream: MediaStream) => {
        const id = ++nextId;
        handed.set(id, stream);
        return id;
    };

    return {
        getUserMedia: (c: RaskMediaConstraints) => mediaDevices.getUserMedia(c),
        attach: (stream: MediaStream, video: HTMLVideoElement | null) =>
            video ? mediaDevices.attach(video, stream) : Promise.resolve(),
        hand,
        take: (id: number) => {
            const stream = handed.get(id);
            handed.delete(id);
            return stream ?? null;
        }
    };
})();
