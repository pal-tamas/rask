# SQLite production pragmas (`Rask.SQLite`)

> **In practice:** [Tutorial Ch 8](tutorial/08-production-sqlite.md) · recipe [turn on production SQLite](recipes.md#turn-on-production-sqlite) · [cheat sheet](cheatsheet.md).

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
> reflection-free, and so works under trimming/AOT.
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
| `trusted_schema` | `OFF` | a schema can carry function calls in views, triggers, index expressions and `CHECK` constraints; `OFF` allows only functions marked innocuous |
| `cell_size_check` | `ON` | catches a corrupt b-tree page as it is read, instead of letting the damage reach your results |
| `analysis_limit` | `400` | bounds `PRAGMA optimize` to a few milliseconds per index |

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

## Keeping the query planner honest — `PRAGMA optimize`

SQLite picks between indexes using statistics in `sqlite_stat1`, and **nothing updates that table on
its own**. A table that was small when it was last analysed — or was never analysed at all — keeps
handing the planner stale numbers, and it starts choosing badly. The classic symptom is a query that
was instant in development crawling in production against real data.

SQLite's own guidance is to run `PRAGMA optimize` before closing a long-lived connection, or
periodically in a long-running process. Rask's EF Core interceptor does it on `ConnectionClosing`,
which for a pooled connection means every return to the pool. It is cheap and self-limiting: it
analyses only what looks stale, bounded by `analysis_limit`, and does nothing when nothing has
changed. It is also best-effort — a connection being torn down must never fail because of an
optimisation — so errors from it are swallowed.

Raw ADO.NET users can call it directly:

```csharp
SqlitePragmas.Optimize(connection);
```

Set `AnalysisLimit = null` to switch the whole thing off, or `0` for no sampling limit (which on a
large table can take a long time).

## STRICT tables — making the store enforce your types

SQLite is dynamically typed. A column's type is an *affinity*, a preference, not a rule: the text
`"lots"` stores happily in an `INTEGER` column, and comes back later as a cast error, a mis-ordered
index, or a silently wrong result. EF Core's model keeps your C# honest, but nothing stops a direct
`INSERT`, an admin tool, a migration script or a legacy row from putting anything anywhere.

[STRICT tables](https://sqlite.org/stricttables.html) (SQLite 3.37+) close that off — the write is
rejected at the source instead. EF Core has no support for them, so Rask supplies a migrations SQL
generator:

```csharp
o.UseRaskSqlite(connectionString, strictTables: true);
```

```sql
CREATE TABLE "Products" (
    "Id"    INTEGER NOT NULL CONSTRAINT "PK_Products" PRIMARY KEY AUTOINCREMENT,
    "Price" TEXT NOT NULL,
    "Name"  TEXT NOT NULL
) STRICT;
```

```text
sqlite> INSERT INTO Products (Price, Name) VALUES (9.95, 'ok');      -- fine
sqlite> UPDATE Products SET Id = 'lots';
Runtime error: cannot store TEXT value in INTEGER column Products.Id
```

**What it costs.** Every column must declare one of exactly six types — `INT`, `INTEGER`, `REAL`,
`TEXT`, `BLOB`, `ANY`. EF Core's default SQLite types are all in that set, so a normal model needs no
changes; an explicit `HasColumnType("VARCHAR(50)")` does. Rask checks this while generating the DDL and
names the table and column at fault, rather than leaving you with SQLite's own message, which names
only the type. Use `ANY` to exempt a single column.

**It is off by default, and on for new apps.** Strictness is a property of a table, decided when the
table is created — so turning it on affects tables created from then on, needs no migration, and
converting an existing table means rebuilding it. `rask new --data` therefore scaffolds it on, where it
is free; an existing database is the case where you have to weigh it.

`decimal` is unaffected: it is `TEXT` in SQLite, which STRICT allows, and it still orders through the
[invariant collation](data-access.md#does-sqlite-support-decimal).

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

On .NET the *mode* half is already handled for you. Microsoft.Data.Sqlite composes its begin statement as
`IsolationLevel == Serializable && !deferred ? "BEGIN IMMEDIATE;" : "BEGIN;"`, and ADO.NET's default
isolation is `Serializable` — so a transaction opened through the driver, or through EF Core (which asks
for `Unspecified`, normalised to `Serializable`), **already takes the write lock up front**. This is
where the .NET driver differs from Ruby's `sqlite3` gem, which defaults to deferred and needed Rails 8 to
change it; nothing in Rask has to ask for the mode. What `Rask.SQLite` adds is the other half — the
*waiting*: a busy handler that frees the thread instead of blocking it.

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
that took the write lock up front. It spells out at the call site what `BeginTransaction()` already does
by default — the value is that it says so, and cannot quietly become deferred if someone passes an
isolation level later. Its wait blocks the thread inside Microsoft.Data.Sqlite, so use
`ExecuteInImmediateTransactionAsync` when you want the non-blocking retry.

Because the lock is taken through the pooled native handle, the path is defensive about connection
reuse: it clears a leaked transaction before **every** `BEGIN IMMEDIATE` attempt, and never hands a
mid-transaction handle back to the pool. Before every attempt rather than once, because a transaction can
appear between passes as well as arrive with the handle — an extended `SQLITE_BUSY` (`BUSY_SNAPSHOT`,
`BUSY_RECOVERY`, which the primary result code hides) can leave `BEGIN`'s own transaction open, and
retrying `BEGIN` inside it fails with the non-retryable "cannot start a transaction within a transaction".
Both rollbacks — the one before each attempt and the one that cleans up on the way out — go through the
same retry, so a rollback blocked by a still-active statement costs a pass instead of poisoning the next
lease. If a statement genuinely fails it throws a `SqliteException` carrying the extended result code, the
autocommit state on entry to the attempt and the attempt number, so a rare failure is attributable rather
than an opaque `SQLite Error 1: 'not an error'` — and so a failure on the hundredth pass reads differently
from one on the first.

**Your callback can run more than once.** SQLite may roll a transaction back *on its own* when a
contended `COMMIT` is answered with `SQLITE_BUSY` — `sqlite3_get_autocommit` is the only way to find
out, and the transaction is simply gone. Retrying the `COMMIT` alone would then meet "cannot commit - no
transaction is active", so the whole transaction is run again from `BEGIN` instead, because everything
the callback wrote went with the rollback. Re-running stops at the same `Timeout`, measured from entry,
after which the loss is surfaced as a `SqliteException` with SQLite's own `SQLITE_ABORT_ROLLBACK`. So:

- Build the callback's commands from state it is **handed**, not state it consumes, so a second pass
  writes the same rows.
- Keep side effects that must not repeat — sending mail, calling a service, incrementing a counter in
  memory — **outside** the transaction.
- Do not issue `COMMIT`, `ROLLBACK` or `END` inside the callback. If the transaction ends while the
  callback is running, whether its writes were kept or discarded cannot be told apart, so that case is
  reported rather than retried.

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
- **You do not have to ask for `IMMEDIATE` here.** Every transaction EF Core opens on SQLite is already
  `BEGIN IMMEDIATE` — the implicit `SaveChanges` one and `Database.BeginTransaction[Async]()` alike (see
  [above](#transactions-begin-immediate--a-non-blocking-fair-interval-retry)). So the lock-upgrade
  dead-lock does not arise on the EF path and there is nothing to wrap. The one deferred trapdoor is
  asking for it: `BeginTransaction(IsolationLevel.ReadUncommitted)`, or Microsoft.Data.Sqlite's
  `deferred: true` overload. All of this is pinned by `RaskSqliteTransactionModeTests` so a driver change
  cannot quietly invalidate it.

> **The read-then-write hazard on the EF path is a different one.** The usual pattern reads *outside* any
> transaction — query, mutate the tracked entity, `SaveChanges` — so there is no read lock to upgrade, but
> equally no protection against another writer having changed the row in between. That is a **lost
> update**, not `SQLITE_BUSY`, and the fix is a concurrency token (`IsConcurrencyToken()` / `[Timestamp]`),
> not a transaction mode. Reading *inside* `Database.BeginTransaction()` closes the window instead, and
> costs you the write lock for the duration of the read.

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

### One database file, or several?

Rask maps the [cache](cache.md), the [job queue](jobs.md), [mail](mail.md) and your own tables into one
`DbContext`, so they share one file. SQLite's write lock is per *file*, so the obvious worry is that the
cache purge sweep and the job-claim batch take the lock a request needs. The alternative is to give the
cache and the queue their own files. This measures what that is worth.

Both arms carry **identical** background churn on their own threads — cache writes, the purge sweep,
enqueues, and a 100-row claim batch — while app writers do the same `INSERT` as `raw-nonblocking` above.
The only difference is whether the churn lands in the app's file or beside it. `idle` runs the batteries at
their shipped defaults (`PollInterval` 5s, `PurgeInterval` 5min, so the sweep never fires in the window);
`busy` is ~500 cache writes/s and ~500 enqueues/s with the sweep compressed to 2s so it is actually
observed. 60s per level, same machine as above.

**Read the `idle` pair first — it is the control.** Those two arms differ only in topology under almost no
churn, so the gap between them *is* the noise floor, and nothing smaller than it counts as a result.

| VUs | arm | ops/s | p99 | p99.9 | max |
|----:|-----|------:|----:|------:|----:|
| 1 | one-file-idle | 78,314 | 0.03 ms | 0.70 ms | 57.70 ms |
| 1 | split-idle | 77,660 | 0.03 ms | 0.72 ms | 111.03 ms |
| 1 | one-file-busy | 70,356 | 0.03 ms | 0.87 ms | 402.73 ms |
| 1 | split-busy | 68,030 | 0.03 ms | 0.79 ms | 380.04 ms |
| 8 | one-file-idle | 67,757 | 1.22 ms | 22.43 ms | 182.79 ms |
| 8 | split-idle | 72,215 | 1.29 ms | 20.51 ms | 133.75 ms |
| 8 | one-file-busy | 43,003 | 2.71 ms | 28.76 ms | 1,338 ms |
| 8 | split-busy | 48,468 | 2.29 ms | 26.32 ms | 1,870 ms |
| 32 | one-file-idle | 62,518 | 17.64 ms | 52.36 ms | 205.98 ms |
| 32 | split-idle | 68,445 | 16.16 ms | 47.11 ms | 297.31 ms |
| 32 | one-file-busy | 65,133 | 16.60 ms | 47.84 ms | 429.80 ms |
| 32 | split-busy | 66,268 | 16.66 ms | 46.86 ms | 147.43 ms |

**The split does not pay for itself here.** The control pair disagrees by up to 9.5% on throughput and by
~2× on max, and *every* `busy` difference sits at or below that floor — 3.3% the wrong way at 1 VU, 12.7%
in favour at 8, 1.7% in favour at 32, with the max column contradicting itself between levels. There is no
effect to report, only variance.

**The interesting number is the one both arms share.** At 8 VUs the churn costs throughput either way —
67,757 → 43,003 on one file, 72,215 → 48,468 on three. If that cost were the shared write lock, splitting
the file would have recovered it; splitting the file recovered nothing. So it is not lock contention. It is
the writes themselves: the same disk, the same page cache, the same fsync budget. **Splitting the file does
not split the disk.**

Two honest limits on this. The harness is closed-loop, and its writers reach ~70k inserts/s — far above any
real app — so the batteries are only ~1.4% of the write volume here, where in a real app the ratio inverts.
That is exactly why the shared-cost decomposition above is the load-bearing result and the per-arm deltas
are not: the decomposition holds whatever the ratio, because the split arm shares no lock at all and still
paid the same price. And the app file's WAL cannot be read as evidence either way — at these write rates the
app alone pins it at the 64 MiB `journal_size_limit` (`split-busy` @ 8 VUs does exactly that, with nothing
but app writes in the file).

What splitting *does* change is size, and that is arithmetic rather than a discovery: the cache table has to
live somewhere, and on one file it lives in the file you replicate. The app database measured 170.5 MiB
against 101.7 MiB on the idle pair — a ~69 MiB cache table sitting inside the thing Litestream ships and
`rask db backup` copies. If you split, split for that, for per-file pragmas (a lost cache is a cache miss,
so it can afford `synchronous=Off`), or to be able to delete a cache file without touching business data.
Not for latency.

One table can never move: the [outbox](outbox.md) writes on the same `DbContext` as the business change so
the two commit together, which is the entire guarantee.

Reproduce with `dotnet run -c Release --project benchmarks/Rask.Benchmarks.Sqlite -- split --vus 1,8,32
--duration 60`.

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
frames flush before a force-kill (`ShutdownGracePeriod`, default 10s) — one rung of
[the shutdown ladder](deployment.md#the-shutdown-ladder), which it must fit inside or the host stops
waiting for it. The `litestream` binary is
driven through [CliWrap](https://github.com/Tyrrrz/CliWrap); a backup failure is logged at `Critical`
but never crashes the app it protects. Point `ConfigPath` at a full `litestream.yml` for multiple
databases or custom retention.

### Checking that backups are running — and that they restore

A `Critical` log line tells you when replication broke; nothing tells you it's *healthy*. Resolve the
`LitestreamStatus` singleton to read that directly:

```csharp
app.MapGet("/health/backup", (LitestreamStatus status) => new { status.Current, status.Verification });
```

| Property | Meaning |
| --- | --- |
| `IsReplicating` | `true` while `litestream replicate` is running. Continuous backup only protects you while this is true. |
| `LastStartedAt` / `LastExitedAt` | When the current (or most recent) run started and ended, UTC. |
| `RestartCount` | How many times replication has been restarted. Above zero means backups were interrupted; **climbing means they're flapping**. |
| `LastExitCode` | The exit code of the most recent run — `null` if it never launched. |
| `LastError` | Why the most recent run failed to launch, if it did. |

A clean shutdown isn't a failure: it clears `IsReplicating` without counting a restart.

#### "Running" is not "restorable"

Every field in that table is a fact about the **local child process**. A replica silently writing to the
wrong prefix, a bucket whose credentials were rotated to read-only, a `-config` file pointing at a
database nobody writes to any more — all of them keep `IsReplicating` true and `RestartCount` flat, and
all of them are discovered at the one moment that matters, which is the restore.

Verification proves the round trip instead of assuming it: write a sentinel into the live database, wait
for replication to carry it, restore the replica **to a temporary path**, and check the sentinel came
back.

```csharp
builder.Services.AddRaskSqliteLitestream(o =>
{
    o.DatabasePath = dbPath;
    o.ReplicaUrl = "s3://my-bucket/app";

    o.Verification.Enabled = true;                      // off by default — see the cost note
    o.Verification.Interval = TimeSpan.FromHours(24);   // a daily audit, not a health poll
});
```

`status.Verification` is `null` until a pass has run — which means *nobody has checked*, not *the backup
is fine* — and then reports:

| Property | Meaning |
| --- | --- |
| `Outcome` | `Verified`, `Inconclusive`, `Failed`, or `Skipped`. |
| `LastVerifiedAt` | When the backup was last **proven restorable**. **This is the field to alert on.** |
| `LastAttemptedAt` | When the most recent pass finished, whatever it concluded. |
| `ReplicationLag` | How long the sentinel took to reach the replica on the last good pass. Creeping towards `Timeout` is your early warning. |
| `LastError` | Why the last pass was inconclusive or failed. |

Three outcomes, not two, and the distinction is the whole point: **`Inconclusive` means the sentinel had
not shipped yet** — replication lag, not a broken backup. Paging on that is how a verification job gets
switched off, so alert on a `LastVerifiedAt` that stops moving instead. `Failed` is the real alert: the
restore itself failed, or it came back without the sentinel.

> **A verification pass costs a restore.** On S3/GCS/Azure that is a real download and a real egress
> bill, which is why it is opt-in, defaults to daily, and must not be wired to an endpoint anything can
> poll. `ISqliteBackupVerifier` is registered whether or not the schedule is on, so you can also trigger
> a pass by hand — from an admin action, or after a deploy that changed the replica configuration.

The sentinel is one upserted row in a `__rask_backup_probe` table (the same row every time, so a database
probed daily for a year is one row heavier), written through the non-blocking busy-retry so it waits out
a busy writer without holding a thread. The restored copy goes to a temp directory that is deleted on
every path, never beside the live database, so a stray `-wal`/`-shm` can't be mistaken for the real thing.

In `-config` mode with several databases there is no single one to probe: set `DatabasePath` to pick one,
or verification reports `Skipped` — the same choice `RestoreSqliteFromLitestreamAsync` already makes.

There is an end-to-end check of all of this against a real object store in
[`scripts/verify-litestream-minio.sh`](../scripts/verify-litestream-minio.sh): it runs MinIO in Docker,
replicates to it, verifies, and then destroys the replica to demonstrate `IsReplicating` staying `true`
while verification reports `Failed`.

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
  the last writes on a redeploy. One caveat worth knowing: with `ServicesStopConcurrently` (what the
  scaffold sets, so the pillars' grace periods overlap rather than sum), Litestream stops alongside a job
  that is still finishing inside *its* grace — so the very last rows such a job writes may not reach the
  replica. Sequential stop wouldn't reliably help, since the order is reverse-registration; and the
  exposure only matters if you lose the machine in those few seconds.

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

To show what you've actually captured, `await store.ListAsync(ct)` returns each snapshot's name, size and
timestamp, newest first — scoped to the same search pattern retention prunes by, so what you can see is what
the store manages. A custom store inherits a default that returns an empty list, so override it if yours can
enumerate: callers can't tell "no snapshots yet" from "this store doesn't list".

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

## SQLite in the browser? (WASM)

SQLite runs in the browser — `Microsoft.Data.Sqlite` and EF Core on top of it, in the tab, with no server
involved. What does **not** belong there is `Rask.SQLite` itself: that package is a **server-side**
one, and deliberately so. Its whole value is the production pragma set — WAL,
`busy_timeout`, `synchronous` — which tames **concurrent** access to a file database. A browser app has
one connection and no concurrency to tame, so those pragmas buy it nothing.

Two things that used to make this impractical no longer do. Linking the native `e_sqlite3` takes no work:
the patched SQLitePCLRaw bundle this repo already pins resolves to a native package that ships the
`browser-wasm` asset and adds the `NativeFileReference` itself. And the database living in memory — lost
on every reload — is what [`Rask.SQLite.Browser`](#rasksqlitebrowser--keeping-a-browser-database) exists
to fix.

For client-side storage in a Rask WASM app, reach for what the browser already gives you and Rask
already wraps:

- **Key/value** — `IBrowserStorage` (localStorage/sessionStorage, ~5 MB) or `IIndexedDb` (hundreds of
  MB, async), both in `Rask.Core.Browser`. See [browser-apis.md](browser-apis.md).
- **A real client-side SQL database, in C#** — [`Rask.SQLite.Browser`](#rasksqlitebrowser--keeping-a-browser-database),
  below. Same `Microsoft.Data.Sqlite`, same EF Core, persisted across reloads.
- **A real client-side SQL database, in JavaScript** — the
  [`sqlite-wasm`](https://sqlite.org/wasm/) + OPFS stack in a Web Worker. A different engine, purpose-built
  for the browser, and still the only way to get SQLite reading and writing OPFS pages directly (which
  needs a Worker — see [Where OPFS fits](#where-opfs-fits)). Reach for it if
  the client-side database is the point of the app rather than a local cache of one.

For a **server** app, none of this changes the advice: keep SQLite behind the server and let the client
talk to it through an API. The browser database is for apps that must
work offline or own their data locally — not a way to avoid having a server.

> **It does run, though: the [playground's tutorial](playground.md#the-guided-tutorial) does exactly
> this.** Its data chapters run EF Core + `Microsoft.Data.Sqlite` in the browser — native relink and all —
> and everything above about *pragmas* still holds, but "it can't work" would be too strong. Two
> constraints if you try it: the app must be **untrimmed** (`PublishTrimmed=true` breaks EF Core, though
> raw ADO.NET survives it), and it needs `NoWarn=WASM0001` for the varargs `sqlite3_config` natives, which
> this repo's warnings-as-errors would otherwise turn into a failed build.
>
> What the playground does *not* solve is durability: its databases live in the runtime's in-memory
> filesystem and are gone on reload, which is the right trade for a teaching sandbox and the wrong one for
> an app. **[`Rask.SQLite.Browser`](#rasksqlitebrowser--keeping-a-browser-database) is the answer to that**,
> and it ships today.

### `Rask.SQLite.Browser` — keeping a browser database

The in-memory filesystem is the whole problem, and it is solvable without changing where the database
lives. `Rask.SQLite.Browser` restores the file from IndexedDB before anything opens it, writes it back on
an interval and on page-hide through SQLite's Online Backup API (never a file copy of a database something
might be writing), and elects a single owner tab with a Web Lock:

```csharp
builder.Services.AddRaskBrowserSqlite("app");
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(BrowserSqlite.ConnectionString("app")));
```

Everything above that line — including [`AddRaskJobs<AppDbContext>()`](jobs.md) — is then the same code you
would write on a server. A worked example, with background jobs running against it and an E2E test that
queues a job and reloads the page: [`samples/Rask.Example.Wasm.Jobs`](../samples/Rask.Example.Wasm.Jobs).

Three limits, stated plainly because each one is a silent failure rather than an error:

- **The durability window is the snapshot interval, not the page-hide flush.** The browser does not wait
  for a `pagehide` handler, so a force-closed or crashed tab loses whatever changed since the last tick.
  Shorten the interval if that matters; each tick copies the whole database, so the cost scales with its
  size rather than with how much changed.
- **Snapshots live in IndexedDB, and IndexedDB is evictable.** Under storage pressure a browser may
  discard them, and the database would come back empty on the next load with nothing to indicate why. The
  owning tab therefore asks for the origin to be exempted (`navigator.storage.persist()`) at startup, and
  logs a refusal rather than failing. Chromium decides from engagement heuristics without prompting;
  **Firefox prompts**, and this is asked during boot rather than from a click — so an app that would
  rather pick its moment sets `o.RequestPersistentStorage = false` and calls
  `IStorageEstimator.RequestPersistAsync()` from a user-gesture handler instead.
- **One tab owns the database.** Every tab has its own copy of the in-memory filesystem, so two owners
  would mean two divergent databases and a last-writer-wins overwrite. The others run with their own empty,
  unpersisted database — which, left unexplained, looks exactly like the user's data having been deleted.
  Inject `BrowserSqliteOwnership` and say so:

  ```csharp
  protected override async Task OnMountAsync() => _isOwner = await ownership.Resolved;
  // ownership.IsOwner is null while the election is in flight, so "deciding" and
  // "not the owner" stay distinguishable and the banner never flashes during a normal boot.
  ```

  When the owner closes, `await ownership.Available` completes in the waiting tab so you can offer a
  reload. **Reloading is what takes it over** — a waiting tab already opened its own empty database at
  boot, so the file cannot be swapped under its live connections, and a tab that started persisting its
  empty database would overwrite the previous owner's good snapshot. Proxying a non-owner's writes to the
  owner is not implemented.
- **The two build settings above are not optional**: `PublishTrimmed=false`, and publishing *without*
  `-p:WasmBuildNative=false` — otherwise SQLite is not linked in and the app boots normally, then fails on
  every database call.

Use `journal_mode=DELETE` rather than WAL here. That is not a WAL workaround: a browser database has one
connection and no reader-while-writer concurrency to protect, and DELETE leaves the file consistent after
every commit — which is exactly what makes its bytes safe to snapshot and ship somewhere else.

#### Where OPFS fits

The [Origin Private File System](browser-apis.md) will make the *flush* incremental:
because it takes ranged writes, a snapshot can write only the 4 KB ranges that changed instead of the whole
file, which is what makes a short interval affordable for a large database. It is a pluggable backend
behind the same `AddRaskBrowserSqlite` call, not a different API. What it will *not* do any time soon is
move the live database onto OPFS: that needs `createSyncAccessHandle`, which exists only inside a Worker,
and Rask boots the .NET runtime on the main thread. So the database stays in the in-memory filesystem, and
restore-at-boot stays a rehydrate — from a different source, and a faster flush.

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
- **Dynamic typing** *(fixable)*. SQLite uses *type affinity*, not strict types: it will happily store
  the text `"lots"` in an `INTEGER` column. EF Core's model gives you type safety in C#, but the store
  itself won't enforce it if something writes to it directly — unless you turn on
  [STRICT tables](#strict-tables--making-the-store-enforce-your-types), which `rask new` does for you.
- **No native `decimal` or `DateTimeOffset`.** Storage classes are `INTEGER`/`REAL`/`TEXT`/`BLOB`/`NULL`
  only. To preserve precision, **EF Core stores a `decimal` as `TEXT`** (not `REAL`, which would round —
  `0.1 + 0.2 ≠ 0.3`), in a culture-invariant format — `19.95`, never `19,95`, whatever the server's
  locale. Arithmetic, comparisons and `Sum`/`Average`/`Min`/`Max` translate through managed helpers EF
  registers on the connection; ordering translates to `ORDER BY "x" COLLATE EF_DECIMAL`. **EF's own
  `EF_DECIMAL` parses that invariant text under `CurrentCulture`**, so on `de-DE` it mis-sorts and on
  `en-HU` it throws inside a native callback and kills the process — [`UseRaskSqlite` replaces it with an
  invariant one](data-access.md#does-sqlite-support-decimal), changing nothing in the file. For money on
  a large, frequently sorted table, integer minor units in an `INTEGER` column still sort and aggregate
  natively and index usefully. EF Core likewise maps `DateTime`/`DateOnly`/`TimeOnly`/`Guid` to `TEXT` —
  mind text-sort ordering.
- **Limited `ALTER TABLE`.** SQLite can add/rename/drop columns but not, say, change a column's type or
  constraints in place — EF Core migrations rebuild the table (create → copy → drop → rename) for those,
  which is slower and briefly locks the table.
- **No server-side surface.** It's an in-process library, not a server: no network endpoint, no
  users/roles/`GRANT`, no stored procedures, no `LISTEN/NOTIFY`. Access control and connection management
  are the app's job.

**Outgrowing it is cheap.** Moving to a client-server database when you genuinely need multi-writer
scale-out or managed HA is a provider + connection-string change you make yourself: the
[`Rask.Data`](data.md) aggregates, [`Rask.Cqrs`](cqrs.md) handlers and generated slices are
provider-agnostic, so point EF Core at your own provider and they follow. Rask ships no provider package
for it — SQLite is the database it wires. What you leave behind is everything on this page that treats the
database as a *file* — Litestream, snapshots, `rask db backup` — and you are off the framework's happy
path. See **[Scaling](scaling.md)** for where the wall actually is and what running more than one instance
costs. SQLite gets you far enough that most single-person products never make the switch.

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
