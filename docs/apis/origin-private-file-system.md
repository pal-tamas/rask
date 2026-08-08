# IOriginPrivateFileSystem

> A private, persistent file tree the app owns — for a local database or any large blob.

- **Wraps:** `navigator.storage.getDirectory` (OPFS)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** — (WebView JS)

Unlike [`IFileSystemAccess`](file-system-access.md) there is no picker and no user gesture. The app
addresses files by path and reopens the same paths on every visit, which is what makes OPFS — not
`IIndexedDb` — the right home for a SQLite file or a downloaded bundle.

```csharp
public sealed class Notes(IOriginPrivateFileSystem fs) : Component
{
    private async Task Save(byte[] page)
    {
        // Parent directories are created on write; nothing has to be made explicitly.
        await fs.WriteAsync("db/app.sqlite", offset: 4096, page);
    }

    private async Task<byte[]?> Load() => await fs.ReadAsync("db/app.sqlite", 4096, 4096);
}
```

## Ranges, not whole files

Reads and writes take a byte offset, so a large file is worked in chunks and the payload crossing the
interop boundary is bounded by the range you ask for. `ReadAllBytesAsync` / `WriteAllBytesAsync` are the
single-round-trip convenience over the same store — prefer the ranged calls once a file grows past a few
megabytes.

A ranged write leaves the rest of the file intact. Writing past the current end extends the file,
zero-filling the gap; `TruncateAsync` resizes in either direction.

## Missing paths return null

Reading a path that does not exist returns `null` rather than throwing, matching
`IKeyValueStore.GetAsync`. A ranged read that runs past the end of the file returns the bytes that were
available — an ordinary short read, not an error.

## Durability

OPFS is persistent but not automatically exempt from eviction: a browser may reclaim it under storage
pressure. Ask for an exemption through [`IStorageEstimator`](storage-estimator.md), and treat unsynced
writes as at risk until it reports `true`.

```csharp
if (!await storage.IsPersistedAsync())
{
    await storage.RequestPersistAsync();
}
```

Chromium decides from engagement heuristics without prompting; Firefox shows a permission prompt, so
call it from a user-gesture handler.

## Transports

Works on both, but every call is a round trip — under the Server transport that round trip crosses the
WebSocket. The local-database scenario this API exists for is in practice a WASM one.

## See also

- Source: [`IOriginPrivateFileSystem.cs`](../../src/Rask.Core/Browser/IOriginPrivateFileSystem.cs)
- [`IFileSystemAccess`](file-system-access.md) — the user-facing picker, for files outside the app
- [`IStorageEstimator`](storage-estimator.md) — quota, usage, and the eviction exemption
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
