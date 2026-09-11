# Rask.Storage — uploaded files on disk, S3 or Azure

> **In practice:** the file picker in [HTTP & files](http-and-files.md#uploading-files) · the
> [dashboard](dashboard.md#what-it-shows)'s Storage tab · [deployment](deployment.md#uploaded-files).

`Rask.Storage` keeps the files your users upload. A saved file is two things: its bytes in a store — a
directory, an S3-compatible bucket or an Azure Blob container — and a **`StoredFile`** row on the app's own
database that says what the bytes are. You inject **`IFiles`**, save the `RaskFile` a file picker hands you,
keep the returned id on your own entity, and later hand the file back as a public URL, a temporary URL, or a
download behind your own authorization check. No cloud SDK is referenced: S3 requests are signed in-process
with SigV4, Azure requests with Shared Key or a service SAS.

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Storage.Off());
> ```

## Why a row per file

Getting bytes from the browser to the server is the easy half, and Rask already does it
([HTTP & files](http-and-files.md)). The hard half is everything after: where the bytes go, what name they
are kept under, who may fetch them later, what `Content-Type` they are served with, and how to find out what
is stored at all. Written by hand, each of those is a decision that fails quietly — an uploaded HTML page
served inline from your own origin, a storage path built from a name the user typed, a directory that fills
with files nothing refers to any more.

A row beside the bytes answers them in one place. The row holds the type sniffed from the content, the size,
the hash and the visibility, so serving a file never has to trust the browser that sent it or the store that
holds it. Your entity refers to a file by id, the way it refers to any other row. And the bytes can live in
a local directory today and a bucket tomorrow without a line of your code changing — only configuration.

## Use

In an app built on `RaskApp` there is nothing to write: the service, the table mapping and the routes are
wired for you. Configure it only where your app differs:

```csharp
app.Configure(c => c.Storage.Configure(o => o.MaxFileSize = 100 * 1024 * 1024));
```

Turning it off keeps the table mapped, like every database-backed battery, so switching it back on later
needs no migration.

A hand-wired host takes three lines. Register the service:

```csharp
// Program.cs
builder.Services.AddRaskStorage<AppDbContext>();
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
```

Map the table:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    modelBuilder.AddRaskStorage();   // maps the StoredFile table
}
```

And map the routes that serve public and temporary links — **after** `UseRask`, because the routes live under
the path base it sets:

```csharp
app.UseRask<App>();
app.MapRaskStorage();
```

Add a migration for the new table before running — `rask db add AddStorage && rask db update`.

`rask new MyApp` scaffolds all of it; `--no-storage` leaves it out. The wiring mistakes are loud rather than
silent: a missing `modelBuilder.AddRaskStorage()` stops the boot and names the line to add, a configuration
value that can't work (a negative size, a storage directory inside `wwwroot`) stops the boot and names what
to change, and a host that never calls `MapRaskStorage()` logs a warning at startup — its public and
temporary links would otherwise 404 with nothing to say why.

## Saving an upload

A file picker hands its handler a list of `RaskFile`s ([uploading files](http-and-files.md#uploading-files)).
Pass one straight to `SaveAsync`:

```csharp
public sealed partial class AvatarPicker(IFiles files) : Component
{
    private string? _avatarUrl;
    private string? _error;

    private async Task OnFilesAsync(IReadOnlyList<RaskFile> picked)
    {
        if (picked.Count == 0)
        {
            return;   // the reader cancelled the picker
        }

        try
        {
            var saved = await files.SaveAsync(picked[0], o => o.Public = true, CancellationToken);
            _avatarUrl = files.Url(saved.Id);
            _error = null;
        }
        catch (FileRejectedException e)
        {
            _avatarUrl = null;
            _error = e.Reason == FileRejection.TooLarge
                ? $"That file is larger than {e.Limit / (1024 * 1024)} MB."
                : "That kind of file isn't accepted here.";
        }
    }

    protected override Component? Render() =>
        Div[
            UiFileInput.Value("").Label("Avatar").Accept("image/*").OnFiles(OnFilesAsync),
            _avatarUrl is null
                ? (Component)P.Class("text-sm")[_error ?? "No avatar yet."]
                : Img.Src(_avatarUrl).Alt("Your avatar")
        ];
}
```

Three things about that handler are load-bearing:

- **Save before the handler returns.** A `RaskFile` is only readable while its handler is on the stack, so
  the `await` belongs inside it.
- **Don't open the stream yourself.** `SaveAsync` opens it with the storage size limit. `RaskFile`'s own
  `OpenReadStream` defaults to a 512 KB cap, which is not the limit you configured.
- **`Accept` is a hint to the dialog, not a check.** The browser's filter and the browser's claimed type are
  both the client's word. What is enforced is what `SaveAsync` sniffs from the bytes — see
  [what is accepted](#what-is-accepted-and-how-it-is-served).

Bytes that don't come from a picker — a generated report, a file fetched from elsewhere — take the stream
overload. The stream is read to its end and not disposed:

```csharp
await using var pdf = await invoices.RenderPdfAsync(invoiceId, ct);
var saved = await files.SaveAsync(pdf, "invoice.pdf", cancellationToken: ct);
```

### Keeping the id

The returned `StoredFile` carries `Id`, the display `Name` (reduced to a safe leaf), the sniffed
`ContentType`, `Size`, the `Sha256` of the bytes, the `Provider` and object `Key`, `Public` and `CreatedAt`
(UTC). Keep the `Id` on your own entity; every other `IFiles` method is addressed by it.

**The row is committed on its own `DbContext`, not inside a transaction your handler has open.** The bytes
are written first and the row second, so a save that fails half way leaves bytes with no row (the
[sweep](#the-orphan-sweep) removes them) and never a row with no bytes. But once `SaveAsync` returns, the
file exists regardless of what your code does next — if the entity that was meant to refer to it then fails
to save, deleting the file is yours to do:

```csharp
var saved = await files.SaveAsync(upload, cancellationToken: ct);
try
{
    order.AttachReceipt(saved.Id);
    await db.SaveChangesAsync(ct);
}
catch
{
    await files.DeleteAsync(saved.Id, CancellationToken.None);
    throw;
}
```

### Size limits along the way

`MaxFileSize` (50 MB by default) is checked twice: before reading, when the upload declared its size, and
again while copying — so a client that under-reports its size is still stopped at the limit. The transport
has limits of its own that apply first, and raising one without the other just moves the refusal:

- the server's upload limit, [`RaskUploadOptions.MaxFileSize`](configuration.md#file-uploads--raskuploads) — 50 MB;
- a remote CQRS upload's [`MaxUploadBytes`](cqrs.md#files-both-directions) — 32 MB.

## Handing a file back

| | Works for | Reaches |
| --- | --- | --- |
| `files.Url(id)` | files saved with `o.Public = true` | anyone with the link, for as long as the file exists |
| `await files.TemporaryUrlAsync(id, lifetime)` | any file | anyone with the link, until it expires |
| `files.Download(id)` | any file | whoever your own endpoint lets through |

### Public URLs

`Url` does no I/O — it is built from the id — so it is safe to call inside `Render()`, as the avatar picker
above does. With nothing configured it returns `{PathBase}/_rask/files/public/{id}`, a route the app serves
itself with `Cache-Control: public, max-age=31536000, immutable`. That header is safe because the bytes
under an id never change: saving again always creates a new id. The route answers `404` for a file that is
not public, exactly as it does for one that does not exist. The bucket behind it stays private — the app
reads the object and streams it, so no object ACL is ever set. Because that response may be cached for a
year, deleting a public file does not take it back from a browser or a CDN that already holds it — anything
you may need to withdraw belongs in a private file handed out with temporary URLs.

Set **`PublicBaseUrl`** to serve public files from a CDN or a public bucket domain instead. `Url(id)` then
returns that URL joined with the file's key, and the request never reaches the app. Prefer an origin other
than the app's own.

> **`PublicBaseUrl` moves the check out of the app, so grant public read on `{Prefix}public/` only.** The
> `Public` flag is enforced by the app's route; a CDN or a publicly readable bucket serves whatever key it is
> asked for. Rask keeps public files under `{Prefix}public/` and private ones under `{Prefix}private/` for
> exactly this reason: a provider-signed temporary URL carries the file's key in plain sight, and a bucket
> policy that also covered `private/` would turn a five-minute link into a permanent one.

### Temporary URLs

```csharp
var link = await files.TemporaryUrlAsync(invoice.PdfId, TimeSpan.FromMinutes(15), CancellationToken);
```

Any file, public or not. The lifetime must be greater than zero and at most seven days; the result is `null`
when there is no such file. What the link *is* depends on where the bytes are:

| Store | The link | The download | Withdrawn by deleting the file |
| --- | --- | --- | --- |
| S3; Azure with an account key | the provider's own signed URL | goes straight from the bucket | **no** — it cannot be revoked before it expires |
| Disk; Azure with a SAS-only connection string | a signed app route, `/_rask/files/{token}` | passes through the app | **yes**, the moment the file is deleted |

A provider-signed link costs the app nothing to serve, and the price is that the app can't take it back.
Choose the lifetime for the worst case of the link being forwarded.

The app-route token is sealed with ASP.NET Core Data Protection and carries only the file id. An expired
token, a tampered one, one for an unknown file and one for a deleted file all answer the same `404`, so a
link reveals nothing about whether a file ever existed. On more than one instance every host must share the
key ring; on a `rask deploy` box Rask already persists it to `/data/keys` on the volume (see
[deployment](deployment.md#your-users-stay-signed-in-across-a-deploy)).

### Downloads behind your own check

For files only some users may see, serve them from an endpoint of your own that checks first, then hands
the response to `Download`:

```csharp
app.MapEndpoints(e => e.MapGet("/invoices/{id:guid}/pdf",
    async (Guid id, ClaimsPrincipal user, Invoices invoices, IFiles files) =>
        await invoices.PdfFileIdForAsync(user, id) is Guid fileId
            ? files.Download(fileId)
            : Results.NotFound()));
```

`Download` streams the file and answers `404` when it is missing. It supports `Range` requests (resumed
downloads, seeking in a video), `If-None-Match` and `HEAD`. Answering `404` rather than `403` to someone who
may not see a file, as above, also keeps from confirming that it exists.

### What every response carries

Whichever of the three serves it, a file goes out with:

- `X-Content-Type-Options: nosniff` — the browser uses the type Rask sniffed, and does not guess another;
- `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox` — should a browser
  render a file as a document anyway, no script runs and it gets no access to your origin;
- `Referrer-Policy: no-referrer` — a signed link is not leaked onward in a `Referer` header;
- a strong `ETag` that is the file's SHA-256, so a repeat request can be answered `304`.

## What is accepted, and how it is served

### The content type is sniffed

The type recorded for a file comes from its bytes — magic numbers, the WHATWG MIME Sniffing patterns for
markup, and a UTF-8 text check. The type the browser claimed is ignored. The file's extension may **narrow** a
sniffed family, never promote it: text named `.csv`, `.md` or `.json` becomes `text/csv`, `text/markdown` or
`application/json`, and a zip named `.docx`, `.xlsx` or `.pptx` becomes that Office type — but a text file
named `photo.png` is still text.

`AllowedTypes` is matched against that sniffed type, as `type/subtype` or `type/*`. Empty, the default,
accepts anything:

```csharp
builder.Services.AddRaskStorage<AppDbContext>(o =>
{
    o.AllowedTypes.Add("image/*");
    o.AllowedTypes.Add("application/pdf");
});
```

An executable renamed `invoice.pdf` is refused by that list, because its bytes don't say PDF. A family never
admits a type that can run script: `image/*` does not let SVG in — name `image/svg+xml` if you mean it.

### Inline or attachment

Only types a browser displays without running anything are served `inline`:

| Family | Types served inline |
| --- | --- |
| Raster images | `image/png`, `image/jpeg`, `image/gif`, `image/webp`, `image/avif` |
| Audio | `audio/mpeg`, `audio/aac`, `audio/ogg`, `audio/wav`, `audio/flac`, `audio/mp4` |
| Video | `video/mp4`, `video/quicktime`, `video/webm` |

Everything else is sent as an `attachment`, so it downloads rather than opens. HTML, SVG and XML are sent as
`application/octet-stream` as well: each can carry script, and a file a user uploaded must never run as a
page on your origin. That is why SVG, which is an image, is not on the inline list.

### Names

The name a file was uploaded with is kept only as a display name, reduced to a safe leaf: no directories, no
control characters, no quotes and no bidirectional-override characters. It never reaches a storage path —
keys are built from the id alone, as `{Prefix}public/` or `{Prefix}private/`, then `{two hex characters}/{id}` — so a name cannot choose where the
bytes land or collide with another file.

### Rejections

A file that is too large or of a type not allowed throws **`FileRejectedException`** (an
`InvalidOperationException`), and **nothing is stored**:

| Member | Meaning |
| --- | --- |
| `Reason` | `FileRejection.TooLarge` or `FileRejection.TypeNotAllowed` |
| `Size`, `Limit` | for `TooLarge`: the bytes read when the limit was passed, and the limit |
| `ContentType` | for `TypeNotAllowed`: the sniffed type |

The message is written for the developer — it names the option to change — and is safe to log: it never
repeats the uploaded file name, which is text a stranger chose. Show the user your own wording, as the
avatar picker does.

## Configuration

Every option can come from configuration under `Storage`, which is how a deployed app picks its store without
a code change. Code in the configure delegate wins over configuration.

| Key | Default | Meaning |
| --- | --- | --- |
| `Storage__Provider` | `Disk` | `Disk`, `S3` or `Azure`. |
| `Storage__MaxFileSize` | `52428800` (50 MB) | The largest file accepted, in bytes. |
| `Storage__PublicBaseUrl` | — | An absolute `https` URL (a CDN or a public bucket domain) that `Url(id)` joins with the file's key. `http` is accepted only for localhost. Unset, the app serves public files itself. |
| `Storage__Prefix` | — | A key prefix, so one bucket can hold several apps (`myapp/`). The sweep never looks outside it. |
| `Storage__Disk__Root` | `/data/files` when the `/data` volume exists, else `storage/` under the content root | Where the disk provider writes. A relative path resolves against the content root; a path inside `wwwroot` is refused. |
| `Storage__S3__ServiceUrl` | — | The S3-compatible endpoint — see [providers](#s3-compatible-storage). |
| `Storage__S3__Bucket` | — | The bucket. |
| `Storage__S3__Region` | `us-east-1` | The signing region. Cloudflare R2 wants `auto`. |
| `Storage__S3__AccessKeyId`, `Storage__S3__SecretAccessKey` | — | The access key pair. |
| `Storage__S3__SessionToken` | — | Only for temporary credentials. |
| `Storage__S3__UsePathStyle` | `true` | Put the bucket in the path rather than the host name. R2 and MinIO need it. |
| `Storage__Azure__ConnectionString` | — | See [Azure Blob](#azure-blob-storage). |
| `Storage__Azure__Container` | — | The container. |

Three more are set in code: `AllowedTypes` (above), `OrphanGracePeriod` and `SweepInterval` (see
[the sweep](#the-orphan-sweep)). The options are validated when the app starts, so a bad value stops the
boot — not the first upload in production.

Secrets go the way every other secret does: `rask deploy --env Storage__S3__SecretAccessKey=…`, remembered by
name and never by value (see [secrets](secrets.md)).

## Providers

### Disk

The default, and the right one for development and for a single box you back up yourself. With no
`Storage__Disk__Root` set, files go to `/data/files` when the `/data` deploy volume exists — so on a
`rask deploy` box they survive a redeploy the way the database does — and to `storage/` under the content
root otherwise.

A root inside `wwwroot` stops the boot: the static-file middleware would serve those files directly, with
none of the checks above, and an uploaded HTML page would be served as HTML from your own origin.

Know the two limits before you rely on it: files on disk are **not backed up**, and the directory is
**node-local**. Both are covered [below](#backups-and-more-than-one-instance).

### S3-compatible storage

One provider covers every store that speaks the S3 API. Requests are signed with SigV4 in-process:

| Service | `ServiceUrl` | Notes |
| --- | --- | --- |
| AWS S3 | `https://s3.<region>.amazonaws.com` | `Region` is the bucket's region. |
| Cloudflare R2 | `https://<account-id>.r2.cloudflarestorage.com` | `Region` is `auto`; path-style (the default). |
| Backblaze B2 | `https://s3.<region>.backblazeb2.com` | An application key with S3 access. |
| MinIO | your server's URL | Path-style (the default). |
| DigitalOcean Spaces | `https://<region>.digitaloceanspaces.com` | |
| Google Cloud Storage | `https://storage.googleapis.com` | Interoperability HMAC keys, not a service-account key file. |

```bash
rask deploy --env Storage__Provider=S3 \
            --env Storage__S3__ServiceUrl=https://<account-id>.r2.cloudflarestorage.com \
            --env Storage__S3__Bucket=shop-files \
            --env Storage__S3__Region=auto \
            --env Storage__S3__AccessKeyId=… \
            --env Storage__S3__SecretAccessKey=…
```

Temporary URLs here are presigned by the provider, so downloads go straight from the bucket.

### Azure Blob Storage

Set `Storage__Azure__ConnectionString` and `Storage__Azure__Container`. The connection string's form decides
how temporary URLs work:

| Connection string | Temporary URLs |
| --- | --- |
| With `AccountName` and `AccountKey` | Signed by Azure as a service SAS; downloads go straight from the container. |
| `BlobEndpoint=…;SharedAccessSignature=…` | Through the app's own signed route — without the account key there is nothing to sign a new SAS with. |
| `UseDevelopmentStorage=true` | Azurite, for local development. |

Requests are signed with Shared Key, or carry the configured SAS.

### Changing provider

Changing `Storage__Provider` does **not** move existing files. Each row records the provider its bytes were
written to, so a file is never looked for in the wrong store: an `IFiles` call for a row from the old provider
throws, naming both providers, and the file routes answer `404`. There is no migration tool — pick the
production store before the first upload you intend to keep.

## Deleting

```csharp
var deleted = await files.DeleteAsync(fileId, ct);   // false when there was no such file
```

The row goes first, then the bytes. Once the row is gone nothing will serve the file, and app-signed
temporary links stop working at that moment; if removing the bytes then fails, they are orphans the sweep
collects. A provider-signed temporary URL is the exception described [above](#temporary-urls).

## The orphan sweep

A hosted service runs about a minute after the app starts and then every `SweepInterval` (24 hours by
default), and removes bytes that have **no row** and
are older than `OrphanGracePeriod` (24 hours by default, at least 5 minutes — longer than any save could still
be in flight between writing the bytes and the row). It is built to do nothing rather than the wrong thing:

- **It fails closed.** Any database error ends the sweep with nothing deleted.
- **It only touches its own keys** — the `{prefix}{public|private}/{two hex}/{id}` layout under the configured prefix. Other
  objects in the same bucket are never considered.
- **It refuses a mass delete.** When a sweep would remove more than 100 objects *and* more than 10% of what it
  looked at, it deletes nothing and logs an error. That shape is the signature of the wrong database (an empty
  or restored `app.db`) or of two environments sharing one bucket and prefix — give each environment its own
  `Prefix`.
- **It deletes nothing against an empty table**, whatever the numbers — an app pointed at a fresh database sees
  every object as an orphan.
- **On S3 or Azure it needs a `Prefix` to delete anything.** Without one it only logs what it found, and the app
  warns at startup: a bucket is easily shared by two environments, and a key's shape alone cannot tell this
  app's orphans from another app's files. Set `Storage__Prefix` for each environment.

## Backups, and more than one instance

**Files on the disk provider are not backed up.** `rask db backup`, Litestream and SQLite snapshots cover
`app.db` and nothing else. The `StoredFile` rows *are* in `app.db`, so a database restored onto a fresh box
comes back pointing at files that are no longer there. Backing up the disk store is not in the box yet. For
uploads you can't afford to lose, use S3 or Azure, where the bytes' durability is the provider's.

**The disk store is node-local.** A file saved on one host does not exist on another, and routing a visitor
back to the same host doesn't help, because a file is read by people other than the one who uploaded it. Run
more than one instance only with S3 or Azure — and, for app-signed temporary links, a shared Data Protection
key ring. See [scaling](scaling.md#if-you-run-more-than-one-host-anyway).

## The operator console

The [dashboard](dashboard.md) at `/_rask` gains a read-only **Storage** tab at `/_rask/storage`: how many files
and bytes are stored, how many are public, usage per provider, and a searchable list of recent files. When
files are on disk it says so, because that is the one state in which no backup covers them. The
`/_rask/files/…` prefix is reserved for the file routes.

## Security notes

- **The browser's word is never used.** The content type is sniffed, the name is reduced to a display leaf,
  and the size is enforced while copying.
- **An upload can't become a page on your origin.** Only raster images, audio and video are served inline;
  HTML, SVG and XML go out as `application/octet-stream`; every response carries `nosniff` and a sandboxing
  Content-Security-Policy; and a disk root inside `wwwroot` is refused.
- **The file routes allow anonymous requests.** The public flag or the signed token *is* the authorization — a
  fallback authorization policy would otherwise break every `<img>` pointing at them. On a hand-wired host you
  can chain your own conventions onto them, such as a rate limit:
  `app.MapRaskStorage().RequireRateLimiting("files")`.
- **Private files go through your code.** Use `Download` from an endpoint that has already checked the user,
  or a short-lived `TemporaryUrlAsync`. Remember that a provider-signed link can't be withdrawn early, and that
  `PublicBaseUrl` exposes every key the bucket serves.
- **Failures say little to strangers.** Every "no" from the file routes is the same `404`, and
  `FileRejectedException`'s message never repeats the uploaded name.

## Limits

- **Server-only.** `Rask.Storage` runs in the ASP.NET host, not in the browser: a WebAssembly page uploads to
  the server, which saves the file.
- **One object per file, no multipart upload.** A single object is at most 5 GiB on S3 and 5000 MiB on Azure,
  so a `MaxFileSize` above the chosen provider's ceiling stops the boot.
- **No image processing.** No resizing, no thumbnails, no format conversion.
- **Disk files are not backed up and don't span instances** — see [above](#backups-and-more-than-one-instance).
- **One store per app,** and changing it doesn't move what is already stored.

## See also

- [HTTP & files](http-and-files.md) — the file picker and `RaskFile`, and `Navigator.Download` for bytes you
  generate on the fly.
- [Forms](forms.md#file-inputs) — file inputs inside a form.
- [Configuration](configuration.md#file-uploads--raskuploads) — the server's own upload limits.
- [Deployment](deployment.md#uploaded-files) — the deploy volume, and passing storage settings.
- [Scaling](scaling.md) — what else changes on more than one host.
- [Dashboard](dashboard.md) — the Storage tab.
- Looking for `localStorage` in the browser? That is [`IBrowserStorage`](apis/storage.md).
