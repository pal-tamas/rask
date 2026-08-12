# Object storage (`Rask.ObjectStore`)

> **In practice:** upload a database snapshot from a server, or sync a static site straight to a bucket
> with no backend at all.

`Rask.ObjectStore` is a small client for S3-compatible and Azure Blob storage with **no cloud SDK behind
it**. It signs SigV4 in-process and takes an Azure SAS as given, which is what lets the same code run
server-side and **in the browser under WebAssembly**.

```bash
dotnet add package Rask.ObjectStore
```

One interface, `IObjectStore`, deliberately kept to the intersection every store agrees on: ranged read,
whole-object stream, write, conditional create, prefix list, delete.

## Providers

| Provider | Registration | Credential |
|---|---|---|
| Amazon S3 | `AddRaskS3ObjectStore` | access key + secret (+ session token) |
| Cloudflare R2 | `AddRaskS3ObjectStore`, `Region = "auto"` | access key + secret |
| Google Cloud Storage | `AddRaskS3ObjectStore` against `storage.googleapis.com` | S3 interop HMAC keys |
| MinIO / Backblaze B2 / Spaces | `AddRaskS3ObjectStore` | access key + secret |
| Azure Blob Storage | `AddRaskAzureBlobObjectStore` | SAS token |
| A folder on disk | `new FolderObjectStore(path)` | none |

```csharp
builder.Services.AddRaskS3ObjectStore(o =>
{
    o.ServiceUrl = new Uri("https://s3.us-east-1.amazonaws.com");
    o.Bucket     = "my-bucket";
    o.Region     = "us-east-1";
});
```

`UsePathStyle` defaults to `true` (`host/bucket/key`) because R2, MinIO and most S3-compatible stores
require it. AWS accepts both; set it to `false` for virtual-host addressing.

### A folder as a bucket

`FolderObjectStore` implements the same interface over a directory:

```csharp
IObjectStore store = new FolderObjectStore("/var/lib/myapp/bucket");
```

Useful in three places, and it is the same code for all of them: running a sample or a test with no
cloud credentials, a single-machine deployment that has no reason to pay for object storage, and — the
interesting one — **a folder something else already replicates**. Point it at a Syncthing share and
devices converge with no central server at all; point it at iCloud Drive, Dropbox or OneDrive and the
replication is somebody else's problem.

Objects are written beside their key and moved into place, so a reader listing the folder concurrently
sees either nothing or the whole object — which matters most when another process is replicating the
folder while it is being written. Keys that would escape the root are refused rather than normalised,
because a key can come from a listing of a folder other people also write to.

`TryCreateAsync` maps to the filesystem's own atomic create, so the [mutual-exclusion](#mutual-exclusion-without-a-lock-service)
pattern below works here too.

## Ranged reads are the point

Object storage charges and waits per byte transferred, so ask for the part you need:

```csharp
// 4 KB at an offset — the rest of a multi-gigabyte object never moves.
var page = await store.GetRangeAsync("db/app.sqlite", offset: 4096, count: 4096);
```

A missing object returns `null`; a range that runs past the end returns the bytes that were there. Those
two cases stay distinguishable, which matters to anything walking an append-only log — "gone" and "no new
bytes yet" must not look alike.

`OpenReadAsync` streams a whole object when you want all of it, and `PutAsync(key, Stream, length)`
uploads without buffering, so object size and memory use stay unrelated.

## Mutual exclusion without a lock service

`TryCreateAsync` writes a key only if nothing is there, and reports whether this caller created it — an
atomic compare-and-create (`If-None-Match: *`) that S3, Azure Blob and GCS all support.

```csharp
if (await store.TryCreateAsync($"rounds/{round}.lock", owner))
{
    // Exactly one caller reaches here.
}
```

Prefer this to a lease. Leases exist only on Azure, need renewal, and leave the resource locked if the
holder disappears; a created key needs none of that and works on every provider.

## Credentials

Supplied through `IObjectStoreCredentials` and asked for **per request**, so an expiring credential can be
refreshed without rebuilding the store.

```csharp
// Server: from configuration.
builder.Services.AddRaskS3ObjectStore(o => { /* ... AccessKeyId / SecretAccessKey ... */ });

// Browser: supplied by the user at runtime, held only in memory.
builder.Services.AddRaskInMemoryObjectStoreCredentials();
builder.Services.AddRaskS3ObjectStore(o => { /* endpoint + bucket only */ });
```

`InMemoryObjectStoreCredentials` never writes the credential anywhere and offers no persistence option: a
credential that survives a reload is one any later script injection can read back, so arriving there has
to be deliberate. `Clear()` is the sign-out path.

**Scope the credential itself.** Read-only where the client only reads, narrowed to one bucket or prefix.
Anything running in the page can do whatever the credential can do, for as long as it is set — that is a
property of having no backend, not a bug to be engineered around.

## Two things that will bite in a browser

**CORS.** Every provider needs the bucket to allow your app's origin, and none do by default. The browser
makes the resulting failure deliberately opaque, so this is the usual reason a configuration that works
from a server does nothing from a page. Configure it on the bucket before debugging anything else.

**Clock skew.** SigV4 rejects any request more than 15 minutes off the service's clock, and device clocks
are wrong often enough to matter. Rather than assume that away, the client reads the service's own `Date`
from a rejected response and signs subsequent requests against corrected time — so a wrong clock costs one
round trip instead of producing a signature error that explains nothing.

## Limits

Multipart upload, presigned URL generation, bucket administration and server-side copy are out of scope.

A key is a path: slashes separate segments, everything else in a segment is escaped. One consequence —
a key whose *name* contains an encoded slash (`%2F`) cannot be addressed, because `System.Uri` normalises
that back to a real separator before any signing code sees it. Such keys are legal in S3 and unreachable
here.

## See also

- [SQLite snapshots](sqlite.md) — what you would upload from a server.
- [Browser APIs](browser-apis.md) — `IOriginPrivateFileSystem` and the rest of the client-side storage story.
