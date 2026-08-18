// Shared file-input plumbing for every in-process host (WASM and Native), spliced at @@RASK_FILES@@.
//
// An <input type=file> hands JS a live File object that cannot cross the interop boundary, so the client
// keeps the File here and ships only metadata plus a short ref. .NET reads the bytes back a chunk at a time
// through that ref, which is what lets RaskFile.OpenReadStream be a real Stream instead of a whole file
// buffered into a render payload.
//
// This lived in rask.wasm.js and made file input a WASM-only capability by accident: the native client had
// no registry, so a shared component's file handler silently received nothing on a native head. Anything
// Rask.Core promises on every host has to live in a module every host splices — this one.
//
// Two readers because the hosts reach it differently: WASM re-exports raskReadFileChunk through a [JSImport]
// that marshals a Uint8Array directly, while the Native host has no such bridge and goes through IJSRuntime,
// which is JSON — hence the base64 variant, reached by identifier as window.__raskFiles.readChunkBase64.

const raskFileRegistry = new Map();

// Registers each File under a fresh ref and returns the metadata .NET turns into RaskFile instances.
// Re-picking on the same input drops that input's previous refs, so a user cycling through files does not
// pile up File objects (and their backing blobs) for the lifetime of the page.
function raskRegisterFiles(inputEl, files) {
    if (inputEl && inputEl.__raskFileRefs) {
        for (const r of inputEl.__raskFileRefs) raskFileRegistry.delete(r);
    }
    const metas = [];
    const refs = [];
    for (const f of files) {
        const r = (typeof crypto !== "undefined" && crypto.randomUUID)
            ? crypto.randomUUID()
            : "f-" + Math.random().toString(36).slice(2);
        raskFileRegistry.set(r, f);
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
async function raskReadFileChunk(ref, offset, length) {
    const file = raskFileRegistry.get(ref);
    if (!file) return new Uint8Array();
    const end = Math.min(file.size, offset + length);
    if (end <= offset) return new Uint8Array();
    const buf = await file.slice(offset, end).arrayBuffer();
    return new Uint8Array(buf);
}

async function raskReadFileChunkBase64(ref, offset, length) {
    const bytes = await raskReadFileChunk(ref, offset, length);
    // Built one bounded slice at a time: String.fromCharCode.apply over a whole chunk blows the argument
    // limit on large reads, and a per-byte string concat is quadratic. 8KB keeps both away.
    let binary = "";
    const step = 8192;
    for (let i = 0; i < bytes.length; i += step) {
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + step));
    }
    return btoa(binary);
}

// The identifier the Native host's IJSRuntime resolves. Harmless on WASM, which uses its [JSImport] export.
if (typeof window !== "undefined") {
    window.__raskFiles = {
        readChunkBase64: raskReadFileChunkBase64
    };
}
