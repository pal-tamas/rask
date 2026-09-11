# Rask.MySql

**MySQL for .NET apps on Entity Framework Core.** `UseRaskMySql(...)` wraps Oracle's `UseMySQL` provider, applies
a row-lock wait and a `SELECT` execution timeout on every connection the context opens, sets a client command
timeout, and turns on transient-failure retrying.

SQLite remains Rask's default, and for most single-developer products it is the right answer for a long time.
This package is for when MySQL is already the house database.

## Install

```bash
dotnet add package Rask.MySql
```

## Use

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskMySql(builder.Configuration.GetConnectionString("App")!)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

Override any default through the optional configure delegate:

```csharp
o.UseRaskMySql(connectionString, m =>
{
    m.CommandTimeout = TimeSpan.FromSeconds(10);
    m.LockTimeout = TimeSpan.FromSeconds(3);
    m.Retry.MaxCount = 3;
});
```

## What the defaults do

| Setting | Default | Why |
|---|---|---|
| `CommandTimeout` | 30s | The client ceiling, and the **only** one on a runaway write: MySQL's statement timeout covers `SELECT` alone. Rounded up to whole seconds, because 0 means "wait forever". |
| `StatementTimeout` | 30s | `max_execution_time` — stops a read-only `SELECT` server-side. Does **not** apply to `INSERT`/`UPDATE`/`DELETE`. `TimeSpan.Zero` leaves it to the server. |
| `LockTimeout` | 10s | `innodb_lock_wait_timeout`, whole seconds. MySQL's own default is 50s, which outlasts the command timeout and reports lock contention as a slow query. Must stay below `CommandTimeout`. |
| `Retry.Enabled` / `MaxCount` / `MaxDelay` | on / 6 / 30s | The provider's own `EnableRetryOnFailure`. |

The driver takes no session variables in the connection string, so the two timeouts are one `SET` sent each time
EF opens a connection — a round trip per open. Code that opens the `DbConnection` directly skips it; open through
`context.Database.OpenConnectionAsync()`. Calling `UseRaskMySql` twice keeps one interceptor and the last call's
settings.

One model convention comes with it. Oracle's provider loses a `DateTimeOffset`'s fractional seconds: it maps one to
whole-second `datetime`, and even from a `datetime(6)` column its reader returns the value truncated to the second.
`UseRaskMySql` stores every `DateTimeOffset` as its UTC `DateTime` in `datetime(6)` — the path that keeps
microseconds — and reads it back as that instant at offset zero, which is what the provider already returned. A
precision or column type you configure is kept; a property with its own value converter is left alone. An app
moving here from a plain `UseMySQL` therefore needs a migration: its `DateTimeOffset` columns become `datetime(6)`.

## Before you choose it

- **Licence.** This package is MIT, but it depends on Oracle's `MySql.EntityFrameworkCore` and `MySql.Data`, which
  are `GPL-2.0-only WITH Universal-FOSS-exception-1.0` and ask for licence acceptance on install.
- **MariaDB is not supported.** It has no `max_execution_time`, so the session `SET` fails on every open.

Retrying is an execution strategy, and like every EF Core retrying strategy it refuses a transaction you opened
yourself outside it: wrap a hand-written `BeginTransaction` in
`context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, or set `m.Retry.Enabled = false`.

## What stays behind

Litestream continuous backup and file snapshots (`Rask.SQLite.Litestream`, `Rask.SQLite.Snapshots`) are
SQLite-only by definition — they replicate and copy *a file*. On MySQL, backup is `mysqldump` or your provider's
snapshots. MySQL commits DDL implicitly, so a migration that fails part-way leaves the schema part-applied; review
generated migrations before running them against production. Migrations are otherwise unchanged: `dotnet ef` (and
`rask db`) are provider-agnostic.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/data.md#mysql>
