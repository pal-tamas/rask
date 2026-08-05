# Rask.Postgres

**PostgreSQL for a Rask app.** `UseRaskPostgres(...)` is a drop-in replacement for `UseNpgsql` that also
wires a `ConnectionOpened` interceptor applying the production session settings — `statement_timeout`,
`lock_timeout`, `idle_in_transaction_session_timeout` — to every connection the context opens, and turns on
transient-failure retrying.

SQLite remains Rask's default, and for most single-developer products it is the right answer for a long
time. This package is the door out of one box: a managed database, a read replica, or more app instances
than one file can serve.

## Install

```bash
dotnet add package Rask.Postgres
```

Or scaffold a project that already uses it:

```bash
rask new Shop --data --database postgres
```

## Use

```csharp
builder.Services.AddDbContextFactory<AppDb>(o =>
    o.UseRaskPostgres(builder.Configuration.GetConnectionString("App")!));
```

Override any default via the optional configure delegate:

```csharp
o.UseRaskPostgres(connectionString, p =>
{
    p.StatementTimeout = TimeSpan.FromSeconds(10);
    p.LockTimeout = TimeSpan.FromSeconds(3);
    p.MaxRetryCount = 3;
});
```

## What the defaults do

| Setting | Default | Why |
|---|---|---|
| `StatementTimeout` | 30s | A runaway query otherwise runs until the client disconnects. |
| `LockTimeout` | 10s | The analogue of SQLite's `busy_timeout`. Without it, a statement stuck behind a lock reports as a *slow query*, which sends you debugging the wrong thing. |
| `IdleInTransactionSessionTimeout` | 1m | An idle-in-transaction session keeps its locks and blocks `VACUUM` from reclaiming dead rows — the usual way a healthy database quietly bloats. |
| `MaxRetryCount` / `MaxRetryDelay` | 6 / 30s | Npgsql's own `EnableRetryOnFailure`, which already knows which PostgreSQL error codes are transient. |

Set any timeout to `TimeSpan.Zero` to leave it alone, so a server-level or role-level setting wins instead
of being overwritten.

## Why there is no `Rask.Postgres` / `Rask.Postgres.EntityFrameworkCore` split

`Rask.SQLite` ships as two packages because the raw ADO layer had to add a busy-retry loop and connection
pooling behaviour that `Microsoft.Data.Sqlite` does not provide. Npgsql already provides both, so there is
nothing for a raw-ADO sibling to add here. One package is the whole story.

## What stays behind

Litestream continuous backup and file snapshots (`Rask.SQLite.Litestream`, `Rask.SQLite.Snapshots`) are
SQLite-only by definition — they replicate and copy *a file*. On PostgreSQL, backup is your provider's
snapshots or `pg_dump`; `rask db backup` will tell you so rather than pretend. `rask db add` / `rask db
update` work unchanged, because they forward to `dotnet ef`.

See [`docs/databases.md`](https://github.com/pal-tamas/rask/blob/main/docs/databases.md) for the full
picture, including what changes when you run more than one instance.
