# SQLite production pragmas (`Rask.SQLite`)

SQLite's stock configuration is tuned for a single embedded process, not a web app. Out of the box a
connection uses the rollback journal (not WAL), does **not** enforce foreign keys, has **no**
`busy_timeout`, and fsyncs on every commit — so the moment two requests write at once you get
`database is locked`, and a bad delete silently orphans rows. The fix is well understood: apply a tuned
pragma set to every connection. **`Rask.SQLite`** does exactly that, applied to every connection you
open, so you get correct, concurrent, production-ready SQLite by default.

The runnable reference is [`samples/Rask.Example.Sqlite`](../samples/Rask.Example.Sqlite).

## Why one server, no PaaS

Tuned like this, SQLite is a real production database, not a test double — and that changes the shape of
what one person has to run. The database is a **file on the same box as the app**: no separate database
tier to provision and pay for, no network hop on every query, no connection pool to a remote server to
size. Reads don't block the writer (WAL), writes wait politely instead of failing (`busy_timeout`), and
[continuous backup](#continuous-backup-with-litestream) streams the file off-box so durability doesn't
depend on that one machine. The result is the One Person Framework payoff: **one server runs the whole
product** — app, data, and background work — with nothing to rent or wire together. See
[the doctrine](one-person-framework.md) for the bigger picture, and
[Limitations & when to outgrow SQLite](#limitations--when-to-outgrow-sqlite) for the honest edges.

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

The defaults are the battle-tested production set for a web-facing SQLite database:

| Pragma | Default | Why |
|--------|---------|-----|
| `journal_mode` | `WAL` | readers don't block the writer — the big concurrency win |
| `synchronous` | `NORMAL` | the safe, fast pairing with WAL |
| `foreign_keys` | `ON` | SQLite leaves referential integrity **off** otherwise |
| `busy_timeout` | `5000` ms | wait out a write lock instead of throwing `database is locked` |
| `cache_size` | `2000` pages (~8 MB) | fewer disk reads per connection |
| `mmap_size` | `128 MiB` | memory-mapped I/O |
| `journal_size_limit` | `64 MiB` | cap WAL growth |
| `temp_store` | *unset* | left on disk by default; opt in with `SqliteTempStore.Memory` |

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

The other half of the concurrency story is *how* you take the write lock. A deferred `BEGIN`
becomes a reader on its first `SELECT` and only upgrades to a writer on its first write — so two
read-then-write transactions can each hold a read lock and then dead-lock trying to upgrade. That
`SQLITE_BUSY` is **unretryable**: SQLite doesn't even invoke the busy handler, because retrying can
never win. `BEGIN IMMEDIATE` takes the write lock up front, turning that dead-lock into a plain,
waitable lock wait.

The established fix is to wait on that lock with a busy handler that **yields instead of blocking**,
retrying at a **constant 1 ms interval** (not exponential backoff, which has far worse tail latency).
`Rask.SQLite` implements both halves.

### Raw ADO.NET — genuinely non-blocking

`ExecuteInImmediateTransactionAsync` runs your work inside a `BEGIN IMMEDIATE` transaction and
acquires the write lock through the raw `sqlite3` handle with the native busy handler off — so the
only waiting is an `await Task.Delay` at the fair interval, which **frees the thread** rather than
blocking it inside native code. It commits when your callback returns and rolls back if it throws:

```csharp
await factory.ExecuteInImmediateTransactionAsync(async (connection, ct) =>
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "INSERT INTO WriteLogs (Note) VALUES ($note);";
    cmd.Parameters.AddWithValue("$note", note);
    await cmd.ExecuteNonQueryAsync(ct);
});
```

Tune the retry when registering (defaults: 5 s timeout, 1 ms interval):

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

Because the lock is taken through the pooled native handle, the path is defensive about connection
reuse: it clears a leaked transaction before `BEGIN IMMEDIATE`, and never hands a mid-transaction handle
back to the pool. If a statement genuinely fails it throws a `SqliteException` carrying the extended
result code and the autocommit state, so a rare failure is attributable rather than an opaque
`SQLite Error 1: 'not an error'`.

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

## Load-test numbers

Everything above is a claim. This section is the evidence: a load harness
(`benchmarks/Rask.Benchmarks.Sqlite`) drives sustained concurrent load against a real database file and
reports throughput, tail latency and error counts. Numbers below are one machine (Apple M4, .NET 10, SSD),
15s per level. Your absolute numbers will differ; the *relationships* are the point, and they are all measured
in the same process on the same box in the same run.

> **How to read these.** The harness is closed-loop — each virtual user (VU) keeps one operation in flight —
> so latency is service time under N concurrent clients. Reproduce any row with
> `dotnet run -c Release --project benchmarks/Rask.Benchmarks.Sqlite -- all`.

### Writes: who waits, and how badly

One `INSERT` per operation, all four write paths against one WAL database.

| VUs | path | ops/s | p50 | p99 | **max** |
|----:|------|------:|----:|----:|--------:|
| 1 | `ExecuteInImmediateTransactionAsync` | 74,145 | 0.01 ms | 0.02 ms | 30 ms |
| 1 | `BEGIN IMMEDIATE` + `busy_timeout` | 93,800 | 0.01 ms | 0.02 ms | 32 ms |
| 32 | `ExecuteInImmediateTransactionAsync` | 68,958 | 0.01 ms | 16 ms | **174 ms** |
| 32 | `BEGIN IMMEDIATE` + `busy_timeout` | 18,186 | 0.03 ms | 0.05 ms | **15,937 ms** |
| 128 | `ExecuteInImmediateTransactionAsync` | 43,441 | 0.01 ms | 68 ms | **408 ms** |
| 128 | `BEGIN IMMEDIATE` + `busy_timeout` | 16,772 | 0.03 ms | 0.68 ms | **15,837 ms** |

Two honest results here, and neither is "the new thing wins everywhere":

- **Uncontended, the native path is faster** (94k vs 74k ops/s). Taking the lock through the raw handle and
  running a managed retry loop is not free. If you have exactly one writer, you are paying ~21% for
  insurance.
- **Contended, the native path's *median* still looks great** — its p50 and p99 beat the non-blocking path,
  because most writers take the lock immediately. The distribution is **bimodal**: the ones that don't get
  stuck behind a thread-blocking wait for nearly **16 seconds**. The non-blocking path trades a worse p99 for
  a worst case that stays bounded (174 ms at 32 VUs, 408 ms at 128) — a **~92× better max** — while also
  sustaining ~3.8× its throughput at 32 VUs.

That is the trade the fair-interval retry actually makes: **not more throughput, a bounded tail** — plus the
freed thread, which a database-only benchmark cannot show but a web server feels (see
`SqliteConcurrencyStressTests`, where 400 writers complete on an 8-thread pool). If your p99 matters more
than your p99.9 and you never starve threads, `busy_timeout` alone is defensible. If a 15-second request is
unacceptable, it is not.

### Readers really do not block the writer

The headline WAL claim, with the control it needs — the same reads in `DELETE` (rollback-journal) mode, which
is what SQLite does if you don't set `journal_mode`:

| readers | journal | writer? | ops/s | p99 |
|--------:|---------|---------|------:|----:|
| 32 | WAL | no | 535,877 | 0.34 ms |
| 32 | WAL | **yes** | **283,936** | **1.17 ms** |
| 32 | DELETE | **yes** | **2,989** | **236 ms** |
| 128 | WAL | no | 460,200 | 4.39 ms |
| 128 | WAL | **yes** | **393,744** | **4.91 ms** |
| 128 | DELETE | **yes** | **2,553** | **1,039 ms** |

With WAL, a writer hammering the same file costs readers ~47% of throughput at 32 readers (and only ~14% at
128), leaving p99 near the idle baseline. Without it, the same readers collapse to **~1%** of their WAL
throughput with a p99 two to three orders of magnitude worse — readers and the writer take turns excluding
each other. This one pragma is worth **~95×** on read throughput under a concurrent writer at 32 readers,
and **~154×** at 128.

### Realistic web traffic: ~90% reads, 10% writes

10,000 seeded rows, a list page (`ORDER BY created_at DESC LIMIT 20`) plus a row fetch for reads, an insert
for writes:

| VUs | path | ops/s | p50 | p99 |
|----:|------|------:|----:|----:|
| 32 | raw ADO | **99,054** | 0.02 ms | 10 ms |
| 32 | EF Core | **26,273** | 0.09 ms | 0.86 ms |
| 128 | raw ADO | 52,765 | 0.09 ms | 65 ms |
| 128 | EF Core | 32,610 | 0.19 ms | 151 ms |

**One process, one file, one disk: ~99k operations/second of realistic mixed traffic at a p99 of 10 ms**, or
~26k/second through EF Core. For scale, that is far past what a single application server will ask of it —
which is the point: SQLite is not the bottleneck you should be designing around.

> One caveat reported rather than buried: the EF mixed arm can rarely throw `SQLITE_BUSY` from
> `SqliteConnection.Close()` — i.e. from *disposing* the `DbContext`, after the work has committed. **This is
> not lock contention, and `busy_timeout` cannot fix it.** When Microsoft.Data.Sqlite returns a pooled
> connection it runs `Deactivate()`, which *un-registers EF Core's built-in helper functions* (`ef_add`,
> `regexp`, the `EF_DECIMAL` collation, …) via `sqlite3_create_function(name, null)`. SQLite refuses that with
> `SQLITE_BUSY` — *"unable to delete/modify user-function due to active statements"* — if any prepared
> statement is still active on the connection, which happens when a reader was GC-collected but its statement
> finalizer has not run yet (`Close()` only finalizes commands it still holds a live reference to). Heavy
> concurrent read/write churn plus a gen2 GC at the wrong instant is what surfaces it. It is an upstream
> Microsoft.Data.Sqlite pool-return behaviour, present for any EF Core SQLite app that registers functions —
> not specific to Rask, and the raw ADO path (no EF functions) shows nothing equivalent. If it bites you,
> `Pooling=False` on the connection string removes it (no pooled return means no `Deactivate`), at the cost of
> re-applying the pragmas on every open. Full deterministic reproduction and analysis are in the harness
> [baselines README](../benchmarks/Rask.Benchmarks.Sqlite/Baselines/README.md).

### Under a sustained soak

32 VUs of mixed traffic for 90 seconds, sampled per 10-second window:

- **The WAL sawtooths and stays small.** It peaked at ~24 MiB against a ~27 MiB database, and latency stayed
  flat. SQLite auto-checkpoints at ~1,000 pages (~4 MB), so `journal_size_limit`'s 64 MiB cap **never
  engages** in a healthy database. It is insurance, not a working limit.
- **A single leaked read transaction is what actually kills you.** Hold one `BEGIN; SELECT …;` open for the
  run and the WAL grows to **3.16 GB in 90 seconds** — against a 0.5 MiB database. A reader pins the WAL's
  oldest needed frame, checkpointing cannot reclaim, and **`journal_size_limit` does not stop it**: that
  limit truncates the WAL *after* a checkpoint, so it cannot cap growth while a checkpoint can't run. This is
  correct SQLite behaviour, and the reason a long-running report or a forgotten transaction is a disk-space
  incident. It truncates back the moment the reader commits.

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

## Limitations & when to outgrow SQLite

SQLite is a genuine production database for a single-server app — but it isn't a client-server database,
and being honest about the boundaries is part of the pitch. Each of these is demonstrated by a test in
[`tests/Rask.SQLite.Limitations.Tests`](../tests/Rask.SQLite.Limitations.Tests) so the behavior is pinned,
not just asserted in prose.

- **One writer at a time.** A write transaction holds the database's single write lock; WAL lets readers
  keep reading *during* a write, but writes serialize. That's plenty for the vast majority of apps — but a
  very write-heavy or highly-concurrent-write workload eventually hits this ceiling. A second
  `BEGIN IMMEDIATE` while another is open gets `SQLITE_BUSY` (which the [transaction helpers](#transactions-begin-immediate--a-non-blocking-fair-interval-retry)
  above wait out); there is no `SELECT … FOR UPDATE SKIP LOCKED`.
- **One machine.** The database is a local file — you cannot scale *writes* across servers. Litestream is
  continuous backup / disaster-recovery + read-replica restore, **not** multi-primary replication. Needing
  several app servers that all write, or managed HA failover, is the boundary.
- **Local disk only.** Never put the database on a network filesystem (NFS/CIFS/SMB) — SQLite's file
  locking is unreliable there and can corrupt the file, and WAL needs real shared memory. Use local disk or
  a single-attach block/named volume, one writer process.
- **Dynamic typing.** SQLite uses *type affinity*, not strict types: it will happily store the text
  `"lots"` in an `INTEGER` column. EF Core's model gives you type safety in C#, but the store itself won't
  enforce it if something writes to it directly.
- **No native `decimal` or `DateTimeOffset`.** Storage classes are `INTEGER`/`REAL`/`TEXT`/`BLOB`/`NULL`
  only. To preserve precision, **EF Core stores a `decimal` as `TEXT`** (not `REAL`, which would round —
  `0.1 + 0.2 ≠ 0.3`). The value round-trips exactly, but a TEXT column doesn't sort or aggregate
  *numerically* in SQL, so server-side `ORDER BY` / `Sum` / `Average` on a decimal is unreliable (EF Core
  warns). For money, store integer minor units instead (the EF Core sample's `Money` value object stores
  cents) — it sorts and sums correctly. EF Core likewise maps `DateTime`/`DateOnly`/`TimeOnly`/`Guid` to
  `TEXT` — mind text-sort ordering.
- **Limited `ALTER TABLE`.** SQLite can add/rename/drop columns but not, say, change a column's type or
  constraints in place — EF Core migrations rebuild the table (create → copy → drop → rename) for those,
  which is slower and briefly locks the table.
- **No server-side surface.** It's an in-process library, not a server: no network endpoint, no
  users/roles/`GRANT`, no stored procedures, no `LISTEN/NOTIFY`. Access control and connection management
  are the app's job.

**Outgrowing it is cheap.** Because Rask's data layer is EF Core, moving to a client-server database
(e.g. PostgreSQL) when you genuinely need multi-writer scale-out or managed HA is largely a provider +
connection-string change — the [`Rask.Data`](data.md) aggregates, [`Rask.Cqrs`](cqrs.md) handlers, and the
generated slices are provider-agnostic. SQLite gets you far enough that most single-person products never
make the switch.

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

> **Careful with `SqliteConnection.ClearAllPools()`.** It is process-global and disposes the underlying
> `sqlite3` handle of connections that are *currently leased and in use*, not just idle ones — so calling
> it while writes are in flight can throw `ObjectDisposedException` from a live connection on another
> thread. In tests, keep pool-clearing teardown from running in parallel with connection-using tests (the
> SQLite test assemblies set `[assembly: CollectionBehavior(DisableTestParallelization = true)]` for this);
> in app code, do not call it on a reset/health-check path that overlaps request handling.

For load rather than correctness, `benchmarks/Rask.Benchmarks.Sqlite` drives sustained concurrent traffic and
reports throughput, tail latency and error counts (the numbers in
[Load-test numbers](#load-test-numbers) above). Its `check` mode is a regression gate over invariants and
same-run ratios — never absolute milliseconds, which are unusable on shared hardware:

```bash
scripts/run-sqlite-load-local.sh          # the gate; run it for any change under src/Rask.SQLite*
```

## Run the sample

```bash
dotnet run --project samples/Rask.Example.Sqlite
# then open the printed URL — the page shows the live pragma values and a concurrent-writes demo
```
