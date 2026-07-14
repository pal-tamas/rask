# SQLite production pragmas (`Rask.SQLite`)

SQLite's stock configuration is tuned for a single embedded process, not a web app. Out of the box a
connection uses the rollback journal (not WAL), does **not** enforce foreign keys, has **no**
`busy_timeout`, and fsyncs on every commit — so the moment two requests write at once you get
`database is locked`, and a bad delete silently orphans rows. Ruby on Rails 8 fixed this by applying
a tuned pragma set to every connection. **`Rask.SQLite`** brings that same set to .NET: swap
`UseSqlite` for `UseRaskSqlite` and you get correct, concurrent, production-ready SQLite by default.

The runnable reference is [`samples/Rask.Example.Sqlite`](../samples/Rask.Example.Sqlite).

> Standalone: `Rask.SQLite` depends only on `Microsoft.Data.Sqlite` and
> `Microsoft.EntityFrameworkCore.Sqlite`. You do **not** need the rest of Rask to use it. Like all
> SQLite access it is a **server-side** story — keep it behind the server, not in a trimmed WASM client
> (see [data-access.md](data-access.md)).

## Install & wire it up

```bash
dotnet add package Rask.SQLite
```

With Entity Framework Core, `UseRaskSqlite` is a drop-in replacement for `UseSqlite` — it configures
the provider *and* registers the pragma interceptor:

```csharp
builder.Services.AddDbContextFactory<AppDb>(o =>
    o.UseRaskSqlite($"Data Source={dbPath}"));
```

For raw ADO.NET (no EF Core), register the connection factory and open connections through it:

```csharp
builder.Services.AddRaskSqlite($"Data Source={dbPath}");

// inject IRaskSqliteConnectionFactory, then:
await using var connection = await factory.CreateOpenAsync(ct);   // pragmas already applied
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

## Transactions: prefer `BEGIN IMMEDIATE` (not handled here)

The other half of Rails' concurrency story is starting write transactions with `BEGIN IMMEDIATE`
rather than the default deferred `BEGIN`, which takes the write lock up front and avoids a
busy-retry deadlock between two upgrading readers. `Rask.SQLite` does not change EF Core's
transaction begin-mode — with the `busy_timeout` above, deferred transactions are fine for most
apps; reach for an explicit `BEGIN IMMEDIATE` on a raw connection only if you see write-write
contention under heavy concurrency.

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

> You need the `litestream` binary on the host — install it, bundle it with your deployment and set
> `ExecutablePath`, or use a container image that includes it.

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
