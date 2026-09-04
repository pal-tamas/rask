// The origin's own private file tree — navigator.storage.getDirectory (OPFS).
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Not the user's disk: this tree is invisible to them, private to the origin, and persists across
// reloads. It is the fastest storage a browser offers and the only one with real ranged reads and
// writes, which is what makes running SQLite in a tab practical.
//
// Paths are "db/app.sqlite" — walked from the private root on every call. There are no handles to
// keep alive, because the tree outlives any handle you would hold.
//
// A missing path is an ordinary answer rather than an error: null, false, or an empty list.

/** A path segment that is not there, or a directory where a file was expected. */
function isMissing(e: unknown): boolean {
    return e instanceof Error && (e.name === "NotFoundError" || e.name === "TypeMismatchError");
}

function segments(path: string | null): string[] {
    return (path || "").split("/").filter((s) => s.length > 0);
}

/** "db/app.sqlite" -> the handle for "db", plus "app.sqlite". */
async function parent(path: string, create: boolean) {
    const parts = segments(path);
    if (parts.length === 0) {
        return null;
    }
    const name = parts.pop();
    let dir = await navigator.storage.getDirectory();
    for (let i = 0; i < parts.length; i++) {
        dir = await dir.getDirectoryHandle(parts[i], {create: !!create});
    }
    return {dir, name};
}

async function fileHandle(path: string, create: boolean): Promise<FileSystemFileHandle | null> {
    const at = await parent(path, create);
    if (!at || !at.name) {
        return null;
    }
    return await at.dir.getFileHandle(at.name, {create: !!create});
}

async function directory(path: string): Promise<FileSystemDirectoryHandle> {
    let dir = await navigator.storage.getDirectory();
    const parts = segments(path);
    for (let i = 0; i < parts.length; i++) {
        dir = await dir.getDirectoryHandle(parts[i], {create: false});
    }
    return dir;
}

/**
 * The async-iterable half of a directory handle.
 *
 * Declared here rather than taken from `lib.dom`, like every other vendor shape in this directory:
 * `keys()` is recent enough that a consumer on an older TypeScript does not have it, and the failure
 * lands as a type error inside Rask's own code during THEIR build. Measured on a scaffolded Next app,
 * whose TypeScript is its own rather than ours.
 */
interface DirectoryHandleWithKeys {
    keys(): AsyncIterableIterator<string>;
}


export function isSupported(): boolean {
    return typeof navigator !== "undefined"
        && !!(navigator.storage && navigator.storage.getDirectory);
}

export async function exists(path: string): Promise<boolean> {
    try {
        return !!(await fileHandle(path, false));
    } catch (e) {
        if (isMissing(e)) {
            return false;
        }
        throw e;
    }
}

/** The file's size in bytes, or null when it is not there. */
export async function size(path: string): Promise<number | null> {
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
}

/**
 * Read `count` bytes from `offset`.
 *
 * `Blob.slice` reads only the requested range, so this never materialises the whole file — which is
 * the difference between paging through a database and loading it. A range past the end returns the
 * bytes that were there, exactly like an ordinary short read.
 */
export async function read(path: string, offset: number, count: number): Promise<Uint8Array | null> {
    try {
        const handle = await fileHandle(path, false);
        if (!handle) {
            return null;
        }
        const file = await handle.getFile();
        return new Uint8Array(await file.slice(offset, offset + count).arrayBuffer());
    } catch (e) {
        if (isMissing(e)) {
            return null;
        }
        throw e;
    }
}

export async function readAll(path: string): Promise<Uint8Array | null> {
    try {
        const handle = await fileHandle(path, false);
        if (!handle) {
            return null;
        }
        return new Uint8Array(await (await handle.getFile()).arrayBuffer());
    } catch (e) {
        if (isMissing(e)) {
            return null;
        }
        throw e;
    }
}

/**
 * Write bytes at `offset`, creating the file and any missing directories.
 *
 * `keepExistingData` is load-bearing rather than an optimisation: without it `createWritable()` starts
 * from an EMPTY file, so a ranged write silently discards every byte outside the range it wrote.
 * Writing past the end zero-fills the gap (File System Standard, write() step 9), which is what lets a
 * growing database write a page beyond its current size.
 */
export async function write(
    path: string,
    offset: number,
    bytes: Uint8Array<ArrayBuffer>): Promise<void> {
    const handle = await fileHandle(path, true);
    if (!handle) {
        return;
    }
    const writable = await handle.createWritable({keepExistingData: true});
    await writable.write({type: "write", position: offset, data: bytes});
    await writable.close();
}

/** Replace the whole file. Starting empty is the default, and here it is what you want. */
export async function writeAll(path: string, bytes: Uint8Array<ArrayBuffer>): Promise<void> {
    const handle = await fileHandle(path, true);
    if (!handle) {
        return;
    }
    const writable = await handle.createWritable();
    await writable.write(bytes);
    await writable.close();
}

export async function truncate(path: string, size: number): Promise<void> {
    const handle = await fileHandle(path, true);
    if (!handle) {
        return;
    }
    const writable = await handle.createWritable({keepExistingData: true});
    await writable.truncate(size);
    await writable.close();
}

/** Remove a file or directory. Removing what is not there is a no-op, not an error. */
export async function remove(path: string, recursive?: boolean): Promise<void> {
    try {
        const at = await parent(path, false);
        if (!at || !at.name) {
            return;
        }
        await at.dir.removeEntry(at.name, {recursive: !!recursive});
    } catch (e) {
        if (isMissing(e)) {
            return;
        }
        throw e;
    }
}

/** The names directly inside a directory. Empty for a path that is not there. */
export async function list(path: string): Promise<string[]> {
    try {
        const names: string[] = [];
        const handle = await directory(path) as unknown as DirectoryHandleWithKeys;
        for await (const name of handle.keys()) {
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
