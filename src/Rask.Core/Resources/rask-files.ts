// Shared file-input plumbing for the in-process host.
//
// An <input type=file> hands JS a live File object that cannot cross the interop boundary, so the client
// keeps the File here and ships only metadata plus a short ref. .NET reads the bytes back a chunk at a time
// through that ref, which is what lets RaskFile.OpenReadStream be a real Stream instead of a whole file
// buffered into a render payload.
//
// It lives in a module every host imports, rather than in rask.wasm.ts, because anything Rask.Core promises
// on every host has to be reachable from every host. WASM re-exports raskReadFileChunk through a [JSImport]
// that marshals a Uint8Array directly.

import { uploadOf } from "./rask-upload.js";

/** The metadata .NET turns into a RaskFile. Field names are the wire contract with the C# record. */
export interface RaskFileMeta {
    ref: string;
    name: string;
    size: number;
    type: string;
    lastModified: number;
}

/**
 * A file input, plus the refs currently registered against it.
 *
 * The expando is how re-picking on the same input finds its own previous entries; it was untyped
 * before, which is why nothing noticed it is the only thing keeping the registry from growing for
 * the lifetime of the page.
 */
interface FileInputWithRefs extends HTMLInputElement {
    __raskFileRefs?: string[];
}

/** A registered File, the input it was chosen in, and how far .NET has read into it. */
interface RegisteredFile {
    file: File;
    input: FileInputWithRefs | null;
    read: number;
}

const raskFileRegistry = new Map<string, RegisteredFile>();

// How much of the files chosen in `input` .NET has read so far — the only progress there is where nothing is
// sent anywhere (rask-upload.ts). Reported on the upload in flight from that input, if one is.
function reportRead(input: FileInputWithRefs | null): void {
    const upload = uploadOf(input);
    if (!upload || !input || !input.__raskFileRefs) return;
    let read = 0, total = 0;
    for (const r of input.__raskFileRefs) {
        const entry = raskFileRegistry.get(r);
        if (entry) { read += entry.read; total += entry.file.size; }
    }
    upload.progress(read, total);
}

// Registers each File under a fresh ref and returns the metadata .NET turns into RaskFile instances.
// Re-picking on the same input drops that input's previous refs, so a user cycling through files does not
// pile up File objects (and their backing blobs) for the lifetime of the page.
export function raskRegisterFiles(
    inputEl: FileInputWithRefs | null,
    files: ArrayLike<File> & Iterable<File>): RaskFileMeta[] {
    if (inputEl && inputEl.__raskFileRefs) {
        for (const r of inputEl.__raskFileRefs) raskFileRegistry.delete(r);
    }

    const metas: RaskFileMeta[] = [];
    const refs: string[] = [];

    for (const f of files) {
        const r = (typeof crypto !== "undefined" && crypto.randomUUID)
            ? crypto.randomUUID()
            : "f-" + Math.random().toString(36).slice(2);
        raskFileRegistry.set(r, {file: f, input: inputEl, read: 0});
        refs.push(r);
        metas.push({
            ref: r,
            name: f.name,
            size: f.size,
            type: f.type || "application/octet-stream",
            lastModified: f.lastModified || 0
        });
    }

    if (inputEl) inputEl.__raskFileRefs = refs;
    return metas;
}

// An unknown ref yields an empty chunk rather than throwing: .NET reads until it gets a short read, so a ref
// invalidated mid-read (the user re-picked while a stream was open) ends the stream instead of faulting it.
export async function raskReadFileChunk(
    ref: string,
    offset: number,
    length: number): Promise<Uint8Array> {
    const entry = raskFileRegistry.get(ref);
    if (!entry) return new Uint8Array();

    const file = entry.file;
    const end = Math.min(file.size, offset + length);
    if (end <= offset) return new Uint8Array();

    const buf = await file.slice(offset, end).arrayBuffer();
    if (end > entry.read) {
        entry.read = end;
        reportRead(entry.input);
    }
    return new Uint8Array(buf);
}
