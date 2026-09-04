// Reading and writing real files on disk — the File System Access API.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// This is the API that lets an editor SAVE, rather than download a second copy into ~/Downloads every
// time. Chromium-family only, secure context, and every picker needs transient user activation.
//
// A picker the user closes rejects with AbortError. That is an ordinary outcome rather than a failure,
// so it resolves null (or an empty array) — a caller writes an if, not a try.

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


export interface FilePickerOptions {
    /** Shown beside the file-type filter in the picker. */
    description?: string | null;
    /** MIME type -> extensions, e.g. {"text/plain": [".txt", ".md"]}. */
    accept?: Record<string, string[]> | null;
    /** Save pickers only. */
    suggestedName?: string | null;
}

/**
 * The pickers, which lib.dom does not declare — Chromium-family only, secure context only.
 * Only what this module drives is described.
 */
interface FilePickers {
    showOpenFilePicker?(options?: {
        multiple?: boolean;
        types?: { description: string; accept: Record<string, string[]> }[];
    }): Promise<FileSystemFileHandle[]>;
    showSaveFilePicker?(options?: {
        suggestedName?: string;
        types?: { description: string; accept: Record<string, string[]> }[];
    }): Promise<FileSystemFileHandle>;
    showDirectoryPicker?(): Promise<FileSystemDirectoryHandle>;
}

function pickers(): FilePickers | null {
    return typeof window === "undefined" ? null : window as unknown as FilePickers;
}

function types(options?: FilePickerOptions | null) {
    if (!options || !options.accept) {
        return undefined;
    }
    return [{description: options.description || "", accept: options.accept}];
}

function isAbort(e: unknown): boolean {
    return e instanceof Error && e.name === "AbortError";
}

/** The picker host, or a refusal naming the call rather than a bare TypeError on undefined. */
function picker(): FilePickers {
    const host = pickers();
    if (!host?.showOpenFilePicker) {
        throw new Error("Rask file system: this browser has no File System Access picker.");
    }
    return host;
}

export function isSupported(): boolean {
    return !!pickers()?.showOpenFilePicker;
}

/** Ask the user for one file. Null if they cancel. */
export async function openFile(options?: FilePickerOptions | null): Promise<FileSystemFileHandle | null> {
    try {
        const picked = await picker().showOpenFilePicker!({multiple: false, types: types(options)});
        return picked[0];
    } catch (e) {
        if (isAbort(e)) {
            return null;
        }
        throw e;
    }
}

/** Ask for several. Empty if they cancel. */
export async function openFiles(options?: FilePickerOptions | null): Promise<FileSystemFileHandle[]> {
    try {
        return await picker().showOpenFilePicker!({multiple: true, types: types(options)});
    } catch (e) {
        if (isAbort(e)) {
            return [];
        }
        throw e;
    }
}

/**
 * Ask where to save. The returned handle stays writable, so a later save needs no second prompt —
 * that is the whole point of the API.
 */
export async function saveFile(options?: FilePickerOptions | null): Promise<FileSystemFileHandle | null> {
    try {
        return await picker().showSaveFilePicker!({
            suggestedName: (options && options.suggestedName) || undefined,
            types: types(options)
        });
    } catch (e) {
        if (isAbort(e)) {
            return null;
        }
        throw e;
    }
}

/** Ask for a directory. Null if they cancel. */
export async function openDirectory(): Promise<FileSystemDirectoryHandle | null> {
    try {
        return await picker().showDirectoryPicker!();
    } catch (e) {
        if (isAbort(e)) {
            return null;
        }
        throw e;
    }
}

export async function readText(handle: FileSystemFileHandle): Promise<string> {
    const file = await handle.getFile();
    return await file.text();
}

export async function readBytes(handle: FileSystemFileHandle): Promise<Uint8Array> {
    const file = await handle.getFile();
    return new Uint8Array(await file.arrayBuffer());
}

/** Overwrite the file. The write only lands when the stream is closed. */
export async function writeText(handle: FileSystemFileHandle, text: string): Promise<void> {
    const writable = await handle.createWritable();
    await writable.write(text);
    await writable.close();
}

/**
 * `Uint8Array<ArrayBuffer>` rather than a bare `Uint8Array`: the type is generic over its backing
 * buffer, and a writable stream will not take a view that might be over SharedArrayBuffer. Anything
 * from `new Uint8Array(...)` or `readBytes` already satisfies it.
 */
export async function writeBytes(
    handle: FileSystemFileHandle,
    bytes: Uint8Array<ArrayBuffer>): Promise<void> {
    const writable = await handle.createWritable();
    await writable.write(bytes);
    await writable.close();
}

/** The names directly inside a directory. */
export async function list(directory: FileSystemDirectoryHandle): Promise<string[]> {
    const names: string[] = [];
    for await (const name of (directory as unknown as DirectoryHandleWithKeys).keys()) {
        names.push(name);
    }
    return names;
}

/** A file inside a directory, optionally creating it. */
export function getFile(
    directory: FileSystemDirectoryHandle,
    name: string,
    create?: boolean): Promise<FileSystemFileHandle> {
    return directory.getFileHandle(name, {create: !!create});
}
