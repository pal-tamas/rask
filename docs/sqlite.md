# SQLite production pragmas (`Rask.SQLite`)

SQLite's stock configuration is tuned for a single embedded process, not a web app. Out of the box a
connection uses the rollback journal (not WAL), does **not** enforce foreign keys, has **no**
`busy_timeout`, and fsyncs on every commit — so the moment two requests write at once you get
`database is locked`, and a bad delete silently orphans rows. Ruby on Rails 8 fixed this by applying
a tuned pragma set to every connection. **`Rask.SQLite`** brings that same set to .NET, applied to
every connection you open, so you get correct, concurrent, production-ready SQLite by default.

The runnable reference is [`samples/Rask.Example.Sqlite`](../samples/Rask.Example.Sqlite).

> Two packages: **`Rask.SQLite`** is the pragma engine — it depends only on `Microsoft.Data.Sqlite`, is
> reflection-free, and so works server-side, on mobile, and under trimming/AOT.
> **`Rask.SQLite.EntityFrameworkCore`** adds the one-line `UseRaskSqlite(...)` for EF Core (and pulls in
> `Microsoft.EntityFrameworkCore.Sqlite`). Neither needs the rest of Rask.

## Install & wire it up

### Raw ADO.NET — `Rask.SQLite`

```bash
dotnet add package Rask.SQLite
```

```csharp
builder.Services.AddRaskSqlite($"Data Source={dbPath}");

// inject IRaskSqliteConnectionFactory, then:
await using var connection = await factory.CreateOpenAsync(ct);   // pragmas already applied
```

### Entity Framework Core — `Rask.SQLite.EntityFrameworkCore`

```bash
dotnet add package Rask.SQLite.EntityFrameworkCore
```

`UseRaskSqlite` is a drop-in replacement for `UseSqlite` — it configures the provider *and* registers
the pragma interceptor:

```csharp
builder.Services.AddDbContextFactory<AppDb>(o =>
    o.UseRaskSqlite($"Data Source={dbPath}"));
```

## The defaults

The defaults are exactly what a modern Rails 8 app runs (verified against
[rails/rails#49349](https://github.com/rails/rails/pull/49349)):

| Pragma | Default | Why |
|--------|---------|-----|
| `journal_mode` | `WAL` | readers don't block the writer — the big concurrency win |
| `synchronous` | `NORMAL` | the safe, fast pairing with WAL |
| `foreign_keys` | `ON` | SQLite leaves referential integrity **off** otherwise |
| `busy_timeout` | `5000` ms | wait out a write lock instead of throwing `database is locked` |
| `cache_size` | `2000` pages (~8 MB) | fewer disk reads per connection |
| `mmap_size` | `128 MiB` | memory-mapped I/O |
| `journal_size_limit` | `64 MiB` | cap WAL growth |
| `temp_store` | *unset* | Rails core leaves it on disk; opt in with `SqliteTempStore.Memory` |

Override any of them, or set one to `null` to leave SQLite's own default:

```csharp
o.UseRaskSqlite($"Data Source={dbPath}", p =>
{
    p.BusyTimeout = TimeSpan.FromSeconds(10);
    p.CacheSize = -20_000;              // negative ⇒ KiB, so 20 MB
    p.TempStore = SqliteTempStore.Memory;
    p.MmapSize = null;                  // leave SQLite's default
});
```

## Why it hooks connection-open (not startup)

Only `journal_mode=WAL` persists in the database file header. Every other pragma
(`foreign_keys`, `busy_timeout`, `synchronous`, `cache_size`, …) is **per connection** and does not
survive the connection closing. Because `Microsoft.Data.Sqlite` **pools** connections, a reused
connection is a fresh open with the stock per-connection defaults — so the pragmas must be re-applied
on *every* open. `Rask.SQLite` does exactly that: an EF Core `ConnectionOpened` interceptor
(`RaskSqliteConnectionInterceptor`) and, for raw ADO.NET, a `StateChange` hook on the factory's
connections. Running them once at startup would silently lose them on the next pooled connection.

## Transactions: `BEGIN IMMEDIATE` + a non-blocking, fair-interval retry

The other half of Rails' concurrency story is *how* you take the write lock. A deferred `BEGIN`
becomes a reader on its first `SELECT` and only upgrades to a writer on its first write — so two
read-then-write transactions can each hold a read lock and then dead-lock trying to upgrade. That
`SQLITE_BUSY` is **unretryable**: SQLite doesn't even invoke the busy handler, because retrying can
never win. `BEGIN IMMEDIATE` takes the write lock up front, turning that dead-lock into a plain,
waitable lock wait.

Rails then waits on that lock with a **busy handler that releases the GVL** between retries at a
**constant 1 ms interval** (not exponential backoff, which has far worse tail latency). `Rask.SQLite`
ports both halves.

### Raw ADO.NET — genuinely non-blocking

`ExecuteInImmediateTransactionAsync` runs your work inside a `BEGIN IMMEDIATE` transaction and
acquires the write lock through the raw `sqlite3` handle with the native busy handler off — so the
only waiting is an `await Task.Delay` at the fair interval, which **frees the thread** (the .NET
equivalent of releasing the GVL). It commits when your callback returns and rolls back if it throws:

```csharp
await factory.ExecuteInImmediateTransactionAsync(async (connection, ct) =>
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "INSERT INTO WriteLogs (Note) VALUES ($note);";
    cmd.Parameters.AddWithValue("$note", note);
    await cmd.ExecuteNonQueryAsync(ct);
});
```

Tune the retry when registering (defaults: 5 s timeout, 1 ms interval — Rails' values):

```csharp
builder.Services.AddRaskSqlite($"Data Source={dbPath}",
    configureRetry: r =>
    {
        r.Timeout = TimeSpan.FromSeconds(10);
        r.PollInterval = TimeSpan.FromMilliseconds(1);
    });
```

For a transaction you drive yourself, `connection.BeginImmediate()` gives you a `SqliteTransaction`
that took the write lock up front (its wait, though, blocks the thread inside Microsoft.Data.Sqlite —
use `ExecuteInImmediateTransactionAsync` when you want the non-blocking retry).

### Entity Framework Core — opt-in retry strategy

Pass `configureRetry` (even empty) to register a fair-interval execution strategy so `SaveChanges`
and queries retry on `SQLITE_BUSY`/`SQLITE_LOCKED`:

```csharp
o.UseRaskSqlite($"Data Source={dbPath}", configureRetry: _ => { });
```

Enabling it turns SQLite's native busy handler off (`busy_timeout=0`) and lowers
Microsoft.Data.Sqlite's own command timeout so the async strategy owns the waiting. Two caveats:

- EF Core issues every command through Microsoft.Data.Sqlite, whose busy-retry is synchronous, so a
  contended attempt can block a thread for up to ~1 s before the fair-interval strategy takes over.
  The raw path above has no such floor.
- The implicit `SaveChanges` transaction stays `DEFERRED` (a write-only batch already takes the write
  lock on its first statement). For a **read-then-write** transaction, wrap it in
  `BeginImmediate` (and, as with any retrying strategy, inside `IExecutionStrategy.ExecuteAsync`).

## Continuous backup with Litestream

WAL mode (now on by default) is exactly what [Litestream](https://litestream.io) needs to
continuously replicate a SQLite database to object storage. The companion package
**`Rask.SQLite.Litestream`** supervises the Litestream sidecar from inside your app — no separate
container to orchestrate:

```bash
dotnet add package Rask.SQLite.Litestream
```

```csharp
var dbPath = "/data/app.db";

builder.Services.AddRaskSqliteLitestream(o =>
{
    o.DatabasePath = dbPath;
    o.ReplicaUrl = "s3://my-bucket/app";     // or gcs://, abs:// (Azure Blob), file:///backups/app
    // o.ExecutablePath = "/usr/local/bin/litestream";  // if it isn't on PATH
});

builder.Services.AddDbContextFactory<AppDb>(o => o.UseRaskSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// Restore from the replica BEFORE opening the DB — a no-op if the file already exists locally.
await app.Services.RestoreSqliteFromLitestreamAsync();

// ... EnsureCreated / migrate / seed ...
app.Run();
```

`AddRaskSqliteLitestream` registers a hosted `BackgroundService` that runs `litestream replicate` for
the lifetime of the process and stops it on shutdown — sending a graceful interrupt so the last WAL
frames flush before a force-kill (`ShutdownGracePeriod`, default 10s). The `litestream` binary is
driven through [CliWrap](https://github.com/Tyrrrz/CliWrap); a backup failure is logged at `Critical`
but never crashes the app it protects. Point `ConfigPath` at a full `litestream.yml` for multiple
databases or custom retention.

### The litestream binary is fetched for you

By default the package **downloads the `litestream` binary at build/publish time** and drops it next to
your app, so there's nothing to install — the default `ExecutablePath` finds it automatically. The
binary for the target runtime (Linux x64/arm64/armv7, macOS x64/arm64, Windows x64/arm64) is fetched
once into a per-user cache (`~/.rask/litestream/<version>/<rid>`), **SHA-256-verified** against a pinned
checksum, and reused by later builds. Knobs (in your project or with `-p:`):

| Property | Purpose |
|---|---|
| `RaskLitestreamDownload` | `false` to skip the download (air-gapped builds); provide your own binary via `ExecutablePath`. |
| `RaskLitestreamVersion` | Pin a different litestream version (also set `RaskLitestreamSha256`). |
| `RaskLitestreamSha256` | The expected checksum when you override the version. |

> Building on a platform litestream doesn't ship (or with the download off)? Install `litestream`
> yourself and point `ExecutablePath` at it — a bundled binary next to the app always wins, otherwise a
> bare `"litestream"` falls back to `PATH`.

### Deploying on Azure App Service Linux (and other ephemeral containers)

This is the scenario Litestream was built for, and it works well — but SQLite has a hard constraint you
must respect:

- **Keep the database on local disk, not the mounted share.** App Service Linux mounts `/home` over a
  network filesystem (Azure Files/CIFS) when `WEBSITES_ENABLE_APP_SERVICE_STORAGE` is on. SQLite WAL
  needs real filesystem locking + shared memory and **does not work over a network share** — you get
  `disk I/O error` / corruption. Put the DB on a local path (e.g. under `/tmp` or the container's own
  filesystem) and let Litestream replicate it to durable storage.
- **Replicate to Azure Blob** with an `abs://container/path` replica URL (Litestream reads the Azure
  credentials from environment variables).
- **Run a single instance.** Litestream assumes one writer — set scale-out to 1. Two instances writing
  their own local databases would diverge the replica.
- **Enable Always On** so the app isn't unloaded when idle. On every cold start / redeploy the local
  disk is empty, and `RestoreSqliteFromLitestreamAsync()` pulls the latest replica back before the app
  opens the database.
- **Graceful shutdown is handled for you.** App Service recycles the container with `SIGTERM`; the
  hosted service interrupts Litestream and lets it flush within `ShutdownGracePeriod`, so you don't lose
  the last writes on a redeploy.

The same recipe applies to any ephemeral-container platform (Kubernetes, Fly.io, Container Apps):
local-disk DB + `abs://`/`s3://` replica + single writer + restore-on-boot.

## Scheduled snapshots

Litestream is continuous streaming replication to object storage. Sometimes you want the other kind of
backup: a **periodic, consistent full copy** you can grab without a sidecar or object storage — "give me
last night's database." The **`Rask.SQLite.Snapshots`** package does exactly that, using SQLite's
**Online Backup API** (never an unsafe `File.Copy` of a live WAL database):

```bash
dotnet add package Rask.SQLite.Snapshots
```

```csharp
builder.Services.AddRaskSqliteSnapshots(o =>
{
    o.DatabasePath = "/data/app.db";
    o.DestinationDirectory = "/backups";
    o.Interval = TimeSpan.FromHours(6);
    o.Retain = 14;                  // keep the 14 newest, prune the rest
    o.SnapshotOnStartup = true;     // also snapshot at boot
});
```

Each snapshot is a complete standalone database (`app-20260714-030000000.db`). Need one on demand — say,
right before a risky migration? Inject `ISqliteSnapshotter` and `await snapshotter.SnapshotAsync(ct)`.
To send snapshots to object storage instead of a local directory, register your own
`ISqliteSnapshotStore` before `AddRaskSqliteSnapshots` (then `DestinationDirectory` isn't required).

**Snapshots vs Litestream — use one, or both:** Litestream is continuous (near-zero data loss, point-in-time
restore) but needs object storage and a sidecar binary; snapshots are periodic, self-contained, need
nothing external, and are trivial to copy or archive. They complement each other — streaming for DR, plus
a retained snapshot history for "restore last Tuesday."

## In a Docker container

All three packages run in a container — with the same care the platform sections above call for:

- **Put the database on a writable local layer or a named volume, never a network mount.** The
  container's own filesystem, a Docker **named volume**, or a bind mount to local disk all give WAL the
  real file locking it needs. An NFS/CIFS-backed volume does not — keep the DB off those.
- **Mount a volume for snapshots** (`DestinationDirectory`) — otherwise the backups live in the
  container's ephemeral layer and vanish on `docker rm`. Or register an object-storage
  `ISqliteSnapshotStore` and skip the volume. `Rask.SQLite` + `Rask.SQLite.Snapshots` need no extra
  binary, so they work on minimal/distroless .NET images (the bundled `e_sqlite3` native lib covers
  Debian/Ubuntu **and** Alpine/musl).
- **Litestream needs its binary in the image.** `COPY` it into your Dockerfile (or copy from the
  official `litestream/litestream` image in a multi-stage build) and set `ExecutablePath` if it isn't on
  `PATH`. Restore-on-boot then rehydrates the DB when a fresh container starts.
- **Single writer.** Don't scale the service to multiple replicas writing the same database
  (`docker compose --scale`, K8s `replicas > 1`) — run one instance.

## SQLite on mobile (Rask.Native)

A [`Rask.Native`](native.md) app runs your C# **natively on the device**, so it can talk to SQLite
directly — and `Rask.SQLite`'s pragmas (WAL, `foreign_keys`, `busy_timeout`) all apply on the device's
real sandbox filesystem. Two things make the **base `Rask.SQLite` package** the right choice here:

- **Reflection-free → AOT-safe.** iOS device builds are fully ahead-of-time compiled, and EF Core's
  `Expression.Compile` crashes there unless you force the Mono interpreter. The raw `AddRaskSqlite`
  path uses only `Microsoft.Data.Sqlite` — no `Expression.Compile`, no reflection — so it just works.
- **Lean.** No Entity Framework Core in the app bundle. (If you do want EF Core on device, add
  `Rask.SQLite.EntityFrameworkCore` and set `<MtouchInterpreter>-all</MtouchInterpreter>` for iOS.)

Register it in the platform head, pointing the database at the app sandbox, **before** `RunLocalAsync`:

```csharp
// Platforms/iOS/AppDelegate.cs or Platforms/Android/MainActivity.cs
var dbPath = Path.Combine(
    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
    "app.db");
host.Services.AddRaskSqlite($"Data Source={dbPath}");
host.Services.AddSingleton<IMyStore, SqliteMyStore>();   // your data service over IRaskSqliteConnectionFactory

_app = await host.RunLocalAsync<MyApp>(webView);
```

The runnable reference is `samples/Rask.Example.Native`, whose **Todos** tab is backed by a
`SqliteTodoStore` on device: the same shared page uses a transient in-memory store on Server/WASM and
the SQLite store on mobile, so adding a todo, killing the app, and relaunching shows it persisted.

> Backup on mobile: use a snapshot (SQLite's Online Backup API) into the sandbox or shared storage —
> **not** Litestream, which spawns a child process (impossible on iOS). WAL still works on-device;
> `Environment.SpecialFolder.LocalApplicationData` maps to the app sandbox (iOS `Library/`, Android
> `filesDir`), which is local storage, so the locking WAL needs is fine.

## SQLite in the browser? (WASM)

`Rask.SQLite` is a **server- and mobile-side** package, and deliberately so. Its whole value is the
production pragma set — WAL, `busy_timeout`, `synchronous` — which tames **concurrent** access to a
file database. A browser (WebAssembly) app has none of that: it's single-threaded, there's no real
filesystem, and **WAL doesn't work** there, so the pragmas that make this package worthwhile don't
apply. Running `Microsoft.Data.Sqlite` in the browser also means compiling and linking a WASM build of
`e_sqlite3` (a native relink at publish) for a database that lives in memory and is lost on reload
unless you serialize the whole file out by hand — a lot of moving parts for little gain.

For client-side storage in a Rask WASM app, reach for what the browser already gives you and Rask
already wraps:

- **Key/value** — `IBrowserStorage` (localStorage/sessionStorage, ~5 MB) or `IIndexedDb` (hundreds of
  MB, async), both in `Rask.Core.Browser`. See [browser-apis.md](browser-apis.md).
- **A real client-side SQL database** — use the JavaScript
  [`sqlite-wasm`](https://sqlite.org/wasm/) + OPFS (Origin Private File System) stack in a Web Worker.
  That's a different engine from `Microsoft.Data.Sqlite`, purpose-built for the browser, and it owns
  the durable-persistence story (OPFS sync access handles) that `Rask.SQLite` intentionally does not
  try to reinvent.

Keep SQLite behind the server (or on device with `Rask.Native`), and let the WASM client talk to it
through an API or the browser storage APIs above.

## Testing

The pragmas are easy to assert against a real database file — open a connection through the factory
or the EF interceptor and read them back:

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = "PRAGMA journal_mode;";
Assert.Equal("wal", cmd.ExecuteScalar());
```

See `tests/Rask.SQLite.Tests` for the unit + integration coverage, and
`tests/Rask.Examples.E2E.Tests/SqliteExampleTests.cs` for the end-to-end concurrent-writes check.

## Run the sample

```bash
dotnet run --project samples/Rask.Example.Sqlite
# then open the printed URL — the page shows the live pragma values and a concurrent-writes demo
```
