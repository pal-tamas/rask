# IIndexedDb

> Large async key/value store.

- **Wraps:** IndexedDB
- **MDN:** [IndexedDB API](https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅

## Text and bytes

`OpenStoreAsync(name)` returns an `IKeyValueStore` with two pairs of accessors:

```csharp
var store = await indexedDb.OpenStoreAsync("cache");

await store.SetAsync("profile", json);              // string
var json2 = await store.GetAsync("profile");

await store.SetBytesAsync("thumbnail", pngBytes);   // byte[]
var png = await store.GetBytesAsync("thumbnail");
```

Use the byte overloads for anything that is not text — an image, a compressed blob, a database file.
They store a real `Uint8Array`, so a megabyte of bytes costs a megabyte of quota; base64 appears only in
transit, because that is what marshals identically across the JS interop boundary on every host.

The two pairs are **not interchangeable**: read a key with the same kind of accessor you wrote it with.

## See also

- Source: [`IIndexedDb.cs`](../../src/Rask.Core/Browser/IIndexedDb.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
