# Choosing a database

Rask is **SQLite-first**, and that is not a limitation to work around — it is the point. One file on the
server's local disk, no network hop to a database tier, and every stateful pillar
([jobs](jobs.md), [mail](mail.md), [cache](cache.md), the [outbox](outbox.md)) riding the same file.
[Why one server, no PaaS](sqlite.md) makes the case with numbers.

This page is about the other door. When one box genuinely isn't enough — a managed database with someone
else on call for it, a read replica, or an app tier you want to scale horizontally — Rask can wire
PostgreSQL or SQL Server instead.

**Most solo products never walk through this door.** Read [the load-test numbers](sqlite.md#load-test-numbers)
before assuming you need to.

## Picking one

```bash
rask new Shop --data                        # SQLite (the default)
rask new Shop --data --database postgres    # PostgreSQL
rask new Shop --data --database sqlserver   # SQL Server
```

You choose once. Everything after that — `rask generate feature`, `rask db`, `rask deploy` — reads the
provider off the project's package references, so there is no second setting to keep in step.

To wire it by hand, swap the one call:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskPostgres(builder.Configuration.GetConnectionString("App")!)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

`UseRaskPostgres` is a drop-in for `UseNpgsql` that also applies the production session settings and turns
on transient-failure retrying — the same role [`UseRaskSqlite`](sqlite.md) plays for pragmas.
`UseRaskSqlServer` does the same for SQL Server.

| Setting | Default | Why |
|---|---|---|
| `StatementTimeout` | 30s | A runaway query otherwise runs until the client disconnects. |
| `LockTimeout` | 10s | The analogue of SQLite's `busy_timeout`. Without it, a statement stuck behind a lock reports as a *slow query* — and you go debugging the wrong thing. |
| `IdleInTransactionSessionTimeout` | 1m | An idle-in-transaction session keeps its locks and blocks `VACUUM` from reclaiming dead rows. The usual way a healthy database quietly bloats. |
| `MaxRetryCount` / `MaxRetryDelay` | 6 / 30s | Npgsql's own `EnableRetryOnFailure`, which already knows which error codes are transient. |

Set any timeout to `TimeSpan.Zero` to leave it alone, so a server- or role-level setting wins.

## What stays behind

Everything Rask does that treats the database as **a file on this machine** has no counterpart on a
client-server database. These are not degraded — there is no file, so there is nothing to do:

| | SQLite | PostgreSQL | SQL Server |
|---|---|---|---|
| Continuous backup ([Litestream](sqlite.md#continuous-backup-with-litestream)) | ✅ | ❌ your provider's backups | ❌ your provider's backups |
| Scheduled snapshots (`--snapshots`) | ✅ | ❌ your provider's snapshots | ❌ your provider's snapshots |
| `rask db backup` / `restore` | ✅ | ❌ `pg_dump` / `pg_restore` | ❌ `BACKUP DATABASE` |
| `/data` volume on deploy | ✅ automatic | ❌ you point at the database | ❌ you point at the database |
| `rask db add` / `update` (migrations) | ✅ | ✅ unchanged | ✅ unchanged |
| Jobs, mail, cache, outbox | ✅ | ✅ | ✅ |
| [`Rask.Data`](data.md) entities + interceptors | ✅ | ✅ | ✅ |

`rask new --snapshots --database postgres` is an **error**, not a silently dropped flag — a backup you
believe you configured is worse than one you know you haven't. `--all-batteries` simply expands to the
batteries that apply.

`rask db backup` refuses too, and says what to use instead. It copies a SQLite file through the Online
Backup API; there is no half-working version worth shipping, because a backup command that quietly does
nothing is the worst bug it could have.

## Deploying

[`rask deploy`](cli.md) mounts a named volume and sets `ConnectionStrings__App` for you on SQLite. On
PostgreSQL it does neither, and **refuses to deploy** unless you supply the connection string:

```bash
rask deploy --env "ConnectionStrings__App=Host=db.internal;Database=shop;Username=app;Password=…"
```

Better, keep it out of your shell history with `--env-file` — see [deployment](deployment.md) and
[secrets](secrets.md). The refusal is deliberate: without it the app would start against the placeholder
connection string compiled into `Program.cs`, which on a server is either nothing or somebody else's
database.

Rask does not provision a database for you. Run one in a container beside the app, or rent a managed one.

## Running more than one instance

The [jobs](jobs.md), [mail](mail.md) and [outbox](outbox.md) processors **lease** the work they claim, so a
job runs on one instance and an email is sent by one instance.

Claiming a batch is one `UPDATE` whose predicate re-tests claimability. Every provider re-evaluates that
predicate against the row version the winner committed, so the row goes to exactly one instance — no
`SKIP LOCKED`, no provider-specific SQL, and the same code path on SQLite. That last part is a claim about
the *server*, so it is tested against real ones: `scripts/run-providers-local.sh` races 20 instances for
200 jobs on both PostgreSQL and SQL Server and asserts no job is claimed twice. A claim marks the rows with a
token and an expiry (`LeaseDuration`, default 5 minutes); finishing hands them back, and so does a graceful
shutdown, so a rolling deploy doesn't park a batch.

**A processor that dies keeps nothing.** Its lease simply runs out and the work becomes claimable again.
There is no sweeper to run and nothing to clean up by hand — expiry *is* the recovery mechanism, which is
why the claim tests the expiry rather than "is this row unclaimed".

### What a lease does not do

> Leases prevent one instance **overwriting another's outcome**. They do not make a side effect happen once.

If an instance overruns its `LeaseDuration`, a second instance may take the row and run the work again
while the first is still going. The first then finds its lease gone and discards its own result — the
database stays consistent — but if the work already sent an email, that email is out. At-least-once was
always the contract; the lease narrows the window from *always, on every instance* to *only when an
instance overruns its lease*.

So: **set `LeaseDuration` comfortably above your slowest handler**, and make handlers idempotent where the
side effect matters. When an overrun does happen you get a warning naming the option to raise:

```
Job 41 lost its lease mid-run on instance …; another instance owns it now.
Increase JobOptions.LeaseDuration past the time this job takes.
```

### `Attempts` counts attempts started

The claim increments `Attempts`, not the failure path. A job that takes the whole process down with it —
an OOM, a pod eviction — never reaches the failure path, so counting only failures would leave it retried
by every instance forever and `MaxAttempts` would never dead-letter it. A job that succeeds first time
shows `Attempts = 1`.

### The request path is a separate question

This is about the *background processors*. Serving traffic from several instances is its own problem:
[live sessions](architecture/live-rendering.md) hold an open WebSocket, a DI scope and a component tree in
one process, so a reconnect must reach the same instance.

### Upgrading an existing app

The lease adds two nullable columns to the jobs, mail and outbox tables. Additive, no backfill — but the
migration is **not optional**:

```bash
rask db add AddLeases
rask db update
```

Skip it and the processors fail on every poll with `no such column: ClaimedUntil`. That failure is caught
and logged rather than crashing the app, so it looks healthy while doing nothing — which is why the log
says exactly these two commands rather than printing a stack trace.

## Testing

`rask generate feature --tests` writes a persistence test that round-trips the entity through a real
database. On SQLite it uses a temp file. On PostgreSQL it creates and drops a uniquely-named database on
the same local server the app's default connection string points at, so `dotnet test` works against a
local PostgreSQL with no extra setup. Point it elsewhere — CI, say — with `RASK_TEST_DB`.

## SQL Server

`--database sqlserver` wires [`Rask.SqlServer`](https://www.nuget.org/packages/Rask.SqlServer). Its
defaults are shaped by what SQL Server actually has, not by symmetry with PostgreSQL:

| Setting | Default | Why |
|---|---|---|
| `CommandTimeout` | 30s | SQL Server has **no server-side statement timeout**, so this client-side ceiling is the only bound on a runaway query. |
| `LockTimeout` | 10s | `SET LOCK_TIMEOUT`. Without it, a statement stuck behind a lock waits out the command timeout and reports as a slow query. |
| `AbortOnError` | `true` | `SET XACT_ABORT ON`. With it off, a statement error inside an explicit transaction leaves that transaction open and holding locks — and the connection goes back to the pool that way. |
| `MaxRetryCount` / `MaxRetryDelay` | 6 / 30s | SQL Server's own `EnableRetryOnFailure`, including the Azure SQL failover error numbers. |

There is no `statement_timeout` equivalent and nothing corresponding to
`idle_in_transaction_session_timeout`, so neither is invented.

One thing to know about isolation: the claim is safe under both locking READ COMMITTED and RCSI. Under
explicit `SNAPSHOT` isolation, SQL Server raises error 3960 rather than allowing a double-claim — the
processor's per-cycle catch turns that into "retry next poll", so it is safe by failure rather than safe by
design.

## Redis, for the cache

Choosing a database provider is separate from choosing where the [cache](cache.md) lives. The cache rides
the app's database by default, and that stays the recommendation — but `ICache` works over any
`IDistributedCache`, so an app that already operates Redis can point it there with two lines and no
`CacheEntry` table:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
builder.Services.AddRaskCache();   // no <AppDbContext>
```

Only the cache. Jobs, mail and the outbox stay on the database — they need transactions, which is the
point of them being there.

### Other providers

None yet. The seam exists and adding one is mostly mechanical, but each provider needs its own production
defaults and its own run against a real server before it can be claimed to work. Rask ships providers it
has actually exercised — see `scripts/run-providers-local.sh`.
