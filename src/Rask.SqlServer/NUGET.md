# Rask.SqlServer

**SQL Server for .NET apps on Entity Framework Core.** `UseRaskSqlServer(...)` is a drop-in replacement for
`UseSqlServer` that applies `SET XACT_ABORT ON` and a `LOCK_TIMEOUT` on every connection the context opens, sets a
client command timeout, turns on transient-failure retrying, and fits Rask's cache key inside SQL Server's index
key limit.

SQLite remains Rask's default, and for most single-developer products it is the right answer for a long time.
This package is for when SQL Server is already the house database.

## Install

```bash
dotnet add package Rask.SqlServer
```

## Use

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlServer(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

`UseRaskSqlServer(sp)` reads the connection string from `Rask:ConnectionStrings:App` — a missing one is an error
naming that key, never a guessed server — and every default below from the `Rask:SqlServer` section:

```jsonc
// appsettings.json
{
  "Rask": {
    "ConnectionStrings": {
      "App": "Server=db;Database=app;User Id=app;TrustServerCertificate=true"
    },
    "SqlServer": {
      "CommandTimeout": "00:00:10",
      "LockTimeout": "00:00:03",
      "Retry": { "MaxCount": 3 }
    }
  }
}
```

In production set them as environment variables (`Rask__ConnectionStrings__App`, `Rask__SqlServer__LockTimeout`). A
configure delegate runs after the section and wins:

```csharp
o.UseRaskSqlServer(sp, s => s.Retry.Enabled = false);
```

## What the defaults do

| Setting | Default | Why |
|---|---|---|
| `CommandTimeout` | 30s | SQL Server has **no server-side statement timeout**, so this client-side ceiling is the only bound on a runaway query. Rounded up to whole seconds, because SqlClient reads 0 as "wait forever". |
| `LockTimeout` | 10s | The analogue of SQLite's `busy_timeout`. Without it a statement stuck behind a lock waits out the command timeout and surfaces as a *slow query*. Must stay below `CommandTimeout`; `TimeSpan.Zero` waits indefinitely (the server default). |
| `AbortOnError` | `true` | `SET XACT_ABORT ON`. With it off, a statement error inside an explicit transaction leaves that transaction open and holding locks — and in a web app the connection returns to the pool in that state. |
| `Retry.Enabled` / `MaxCount` / `MaxDelay` | on / 6 / 30s | SQL Server's own `EnableRetryOnFailure`, which already knows the transient error numbers, the Azure SQL failover set included. |

SQL Server takes no session settings in the connection string, so `XACT_ABORT` and `LOCK_TIMEOUT` are one batch
sent each time EF opens a connection — a round trip per open, since SqlClient resets a pooled connection's state.
Code that opens the `DbConnection` directly skips it; open through `context.Database.OpenConnectionAsync()`.

Retrying is an execution strategy, and like every EF Core retrying strategy it refuses a transaction you opened
yourself outside it: wrap a hand-written `BeginTransaction` in
`context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, or set `s.Retry.Enabled = false`.

## The cache key

A SQL Server index key holds at most 900 bytes — 450 `nvarchar` characters. `Rask.Cache` configures its key at
512 so SQLite and PostgreSQL keep the room, which SQL Server only admits with a warning. `UseRaskSqlServer` caps
that one key at 450 in the model; no other entity is touched, and SQLite apps get no migration. A longer key still
cannot be stored on SQL Server — `ICache` rejects it with an error naming the limit, so hash long keys.

## What stays behind

Litestream continuous backup and file snapshots (`Rask.SQLite.Litestream`, `Rask.SQLite.Snapshots`) are
SQLite-only by definition — they replicate and copy *a file*. On SQL Server, backup is `BACKUP DATABASE` or your
provider's snapshots. Migrations are unchanged: `dotnet ef` (and `rask db`) are provider-agnostic.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/data.md#sql-server>
