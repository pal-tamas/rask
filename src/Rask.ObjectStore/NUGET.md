# Rask.ObjectStore

An object-storage client with no cloud SDK behind it. Ranged reads, streaming writes, conditional
create, prefix listing and delete — over **Amazon S3 and everything that speaks its API** (Cloudflare
R2, Google Cloud Storage via its S3 interop keys, MinIO, Backblaze B2, DigitalOcean Spaces) or
**Azure Blob Storage**.

It signs SigV4 itself and takes an Azure SAS as given, which is what lets it run **in the browser under
WebAssembly** as well as on a server. The same client uploads a database snapshot from a host, or syncs
a static site straight to a bucket with no backend in between.

```bash
dotnet add package Rask.ObjectStore
```

## Use it

```csharp
builder.Services.AddRaskS3ObjectStore(o =>
{
    o.ServiceUrl = new Uri("https://s3.us-east-1.amazonaws.com");
    o.Bucket     = "my-bucket";
    o.Region     = "us-east-1";
});
```

```csharp
public sealed class Snapshots(IObjectStore store)
{
    // Ranged: only the bytes you ask for cross the wire.
    public Task<byte[]?> ReadPageAsync(long offset) =>
        store.GetRangeAsync("db/app.sqlite", offset, 4096);

    // Streamed: an arbitrarily large object costs no memory.
    public async Task UploadAsync(Stream file, long length) =>
        await store.PutAsync("snapshots/latest.db", file, length);
}
```

Azure is the same interface, registered differently:

```csharp
builder.Services.AddRaskAzureBlobObjectStore(o =>
{
    o.ServiceUrl = new Uri("https://myaccount.blob.core.windows.net");
    o.Bucket     = "data";                 // the container
    o.SasToken   = configuration["Sas"];   // read/write/list
});
```

## Credentials

Supplied through `IObjectStoreCredentials` and asked for **per request**, so a credential that expires —
an STS session, a time-boxed SAS — can be refreshed without rebuilding the store.

- **From configuration** (server): set `AccessKeyId`/`SecretAccessKey` (and optionally `SessionToken`),
  or `SasToken`, on `ObjectStoreOptions`. This is the default.
- **Supplied at runtime** (browser): register `AddRaskInMemoryObjectStoreCredentials()` *before* the
  store, then call `Set(...)` once the credential is known.

`InMemoryObjectStoreCredentials` holds the credential for the life of the process and never writes it
anywhere. There is deliberately no persistence option: a credential that survives a reload is a
credential any later script injection can read back, so making that happen has to be a deliberate act.

Scope the credential itself — read-only where the client only reads, narrowed to one bucket or prefix.
Anything running in the page can do whatever the credential can do, for as long as it is set.

## Mutual exclusion without a lock service

`TryCreateAsync` writes a key only if nothing is there yet, and reports whether this caller was the one
that created it. It is an atomic compare-and-create (`If-None-Match: *`) supported by S3, Azure Blob and
GCS alike — enough to elect one writer or run a job once, with nothing to renew and nothing left behind
if the winner disappears.

```csharp
if (await store.TryCreateAsync($"rounds/{round}.lock", owner))
{
    // Exactly one caller gets here.
}
```

## Two things that will bite in a browser

- **CORS.** Every provider requires the bucket to allow your app's origin, and none do by default. The
  browser deliberately makes the resulting failure opaque, so this is the usual reason a configuration
  that works from a server does nothing from a page.
- **Clock skew.** SigV4 rejects a request more than 15 minutes off the service's clock. Device clocks
  are wrong often enough that this is handled rather than assumed away: the service's own `Date` is read
  from the response and later requests sign against corrected time.

## What it does not do

Multipart upload, presigned URL generation, bucket administration, and server-side copy. This is the
small intersection every store agrees on, kept small on purpose.

---

Part of [Rask](https://github.com/pal-tamas/rask). Usable on its own — it depends only on
`Microsoft.Extensions.*` and takes no dependency on the rest of the framework.
