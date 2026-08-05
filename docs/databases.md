# Choosing a database

Rask is **SQLite-first**, and that is not a limitation to work around — it is the point. One file on the
server's local disk, no network hop to a database tier, and every stateful pillar
([jobs](jobs.md), [mail](mail.md), [cache](cache.md), the [outbox](outbox.md)) riding the same file.
[Why one server, no PaaS](sqlite.md) makes the case with numbers.

This page is about the other door. When one box genuinely isn't enough — a managed database with someone
else on call for it, a read replica, or an app tier you want to scale horizontally — Rask can wire
PostgreSQL instead.

**Most solo products never walk through this door.** Read [the load-test numbers](sqlite.md#load-test-numbers)
before assuming you need to.

## Picking one

```bash
rask new Shop --data                        # SQLite (the default)
rask new Shop --data --database postgres    # PostgreSQL
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

| | SQLite | PostgreSQL |
|---|---|---|
| Continuous backup ([Litestream](sqlite.md#continuous-backup-with-litestream)) | ✅ | ❌ your provider's backups |
| Scheduled snapshots (`--snapshots`) | ✅ | ❌ your provider's snapshots |
| `rask db backup` / `restore` | ✅ | ❌ `pg_dump` / `pg_restore` |
| `/data` volume on deploy | ✅ automatic | ❌ you point at the database |
| `rask db add` / `update` (migrations) | ✅ | ✅ unchanged |
| Jobs, mail, cache, outbox | ✅ | ✅ |
| [`Rask.Data`](data.md) entities + interceptors | ✅ | ✅ |

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

> **Not yet safe, on any provider.** Run a **single instance**.

The [jobs](jobs.md), [mail](mail.md) and [outbox](outbox.md) processors poll for due work and run it
without claiming or leasing it first. On SQLite that constraint is natural and documented — one writer,
one processor per app. PostgreSQL invites `replicas > 1`, and there it becomes a trap: **two instances run
every job and send every email twice.**

Leased claiming — where an instance takes a batch for a bounded time, and a crashed instance's work
becomes claimable again — is tracked in [#552](https://github.com/pal-tamas/rask/issues/552). Until it
ships, scale vertically and keep `replicas: 1`.

Note this is about the *background processors*, not the request path. Serving traffic from several
instances is a separate question: [live sessions](architecture/live-rendering.md) hold an open WebSocket, a
DI scope and a component tree in one process, so a reconnect must reach the same instance.

## Testing

`rask generate feature --tests` writes a persistence test that round-trips the entity through a real
database. On SQLite it uses a temp file. On PostgreSQL it creates and drops a uniquely-named database on
the same local server the app's default connection string points at, so `dotnet test` works against a
local PostgreSQL with no extra setup. Point it elsewhere — CI, say — with `RASK_TEST_DB`.

## SQL Server, and others

Not yet. The provider seam exists and adding one is mostly mechanical, but each provider needs its own
production defaults and its own test run before it can be claimed to work. Rask ships providers it has
actually exercised.
