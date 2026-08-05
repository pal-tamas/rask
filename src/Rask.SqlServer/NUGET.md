# Rask.SqlServer

**SQL Server for a Rask app.** `UseRaskSqlServer(...)` is a drop-in replacement for `UseSqlServer` that also
wires a `ConnectionOpened` interceptor applying the production session settings — `SET LOCK_TIMEOUT`,
`SET XACT_ABORT ON` — to every connection the context opens, sets a client command timeout, and turns on
transient-failure retrying.

SQLite remains Rask's default, and for most single-developer products it is the right answer for a long
time. This package is for the case where SQL Server is already the house database.

## Install

```bash
dotnet add package Rask.SqlServer
```

Or scaffold a project that already uses it:

```bash
rask new Shop --data --database sqlserver
```

## Use

```csharp
builder.Services.AddDbContextFactory<AppDb>(o =>
    o.UseRaskSqlServer(builder.Configuration.GetConnectionString("App")!));
```

Override any default via the optional configure delegate:

```csharp
o.UseRaskSqlServer(connectionString, p =>
{
    p.CommandTimeout = TimeSpan.FromSeconds(10);
    p.LockTimeout = TimeSpan.FromSeconds(3);
    p.AbortOnError = false;
});
```

## What the defaults do

| Setting | Default | Why |
|---|---|---|
| `CommandTimeout` | 30s | SQL Server has **no server-side statement timeout**, so this client-side ceiling is the only bound on a runaway query. |
| `LockTimeout` | 10s | The analogue of SQLite's `busy_timeout`. Without it, a statement stuck behind a lock waits out the command timeout and surfaces as a *slow query* — so you go reading query plans instead of finding what holds the lock. |
| `AbortOnError` | `true` | `SET XACT_ABORT ON`. With it off, a statement error inside an explicit transaction leaves that transaction open and holding locks until something rolls it back — and on a web app the connection returns to the pool in that state. |
| `MaxRetryCount` / `MaxRetryDelay` | 6 / 30s | SQL Server's own `EnableRetryOnFailure`, which already knows the transient error numbers including the Azure SQL failover set. |

`LockTimeout` is validated to sit below `CommandTimeout`; above it the client gives up first and the lock
signal is lost. Set `LockTimeout` to `TimeSpan.Zero` to wait indefinitely (the server default).

## Not a mirror of `Rask.Postgres`

Deliberately. SQL Server has no server-side statement timeout and nothing corresponding to
`idle_in_transaction_session_timeout`, so neither is invented here; what it does have, and PostgreSQL does
not need, is `XACT_ABORT`. The two packages match in shape, not in knobs.

## What stays behind

Litestream continuous backup and file snapshots (`Rask.SQLite.Litestream`, `Rask.SQLite.Snapshots`) are
SQLite-only by definition — they replicate and copy *a file*. On SQL Server, backup is `BACKUP DATABASE` or
your provider's snapshots; `rask db backup` will tell you so rather than pretend. `rask db add` /
`rask db update` work unchanged, because they forward to `dotnet ef`.

See [`docs/databases.md`](https://github.com/pal-tamas/rask/blob/main/docs/databases.md) for the full
picture, including what changes when you run more than one instance.
