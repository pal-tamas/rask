// IndexedDB, as a key/value store — indexedDB.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Each named store is its own IndexedDB database holding a single object store, and the open
// connection is cached. Every operation resolves when the TRANSACTION commits rather than when the
// request succeeds, which is the difference between "the browser accepted this" and "this survives a
// reload".
//
// Bytes are Uint8Array here. The base64 hop exists only because that is the one encoding that
// marshals identically across .NET's two interop transports, so it lives in ./globals.ts with the
// rest of the calling convention — storing base64 text would also cost about a third of the origin's
// quota for every byte, which matters once the value is something like a database file.

const STORE = "kv";

const dbs = new Map<string, Promise<IDBDatabase>>();

function open(name: string): Promise<IDBDatabase> {
    const cached = dbs.get(name);
    if (cached) {
        return cached;
    }
    const p = new Promise<IDBDatabase>((resolve, reject) => {
        const req = indexedDB.open(name, 1);
        req.onupgradeneeded = () => { req.result.createObjectStore(STORE); };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
    dbs.set(name, p);
    return p;
}

/** Run fn(objectStore) in a transaction; resolve with the request's result once it COMMITS. */
function run(
    name: string,
    mode: IDBTransactionMode,
    fn: (store: IDBObjectStore) => IDBRequest | undefined): Promise<unknown> {
    return open(name).then((db) => new Promise<unknown>((resolve, reject) => {
        const t = db.transaction(STORE, mode);
        const req = fn(t.objectStore(STORE));
        t.oncomplete = () => resolve(req && req.result !== undefined ? req.result : null);
        t.onerror = () => reject(t.error);
        t.onabort = () => reject(t.error);
    }));
}

/** A single named store: an async key/value map, backed by its own IndexedDB database. */
export interface KeyValueStore {
    get(key: string): Promise<unknown>;
    set(key: string, value: unknown): Promise<void>;
    getBytes(key: string): Promise<Uint8Array | null>;
    setBytes(key: string, bytes: Uint8Array): Promise<void>;
    remove(key: string): Promise<void>;
    keys(): Promise<IDBValidKey[]>;
    clear(): Promise<void>;
}

export function isSupported(): boolean {
    return typeof indexedDB !== "undefined";
}

/**
 * Open (creating if needed) a named store. Awaiting this is what forces the `upgradeneeded`
 * round trip, so a caller that opens once and keeps the handle pays for it once.
 */
export async function openStore(name: string): Promise<KeyValueStore> {
    await open(name);
    return {
        get: (key) => run(name, "readonly", (s) => s.get(key)).then((v) => (v === undefined ? null : v)),
        set: (key, value) => run(name, "readwrite", (s) => s.put(value, key)).then(() => undefined),
        getBytes: (key) =>
            run(name, "readonly", (s) => s.get(key))
                .then((v) => (v === undefined || v === null ? null : (v as Uint8Array))),
        setBytes: (key, bytes) =>
            run(name, "readwrite", (s) => s.put(bytes, key)).then(() => undefined),
        remove: (key) => run(name, "readwrite", (s) => s.delete(key)).then(() => undefined),
        keys: () => run(name, "readonly", (s) => s.getAllKeys()).then((k) => (k || []) as IDBValidKey[]),
        clear: () => run(name, "readwrite", (s) => s.clear()).then(() => undefined)
    };
}
