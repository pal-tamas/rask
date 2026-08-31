# Rask.SQLite

**Production-ready SQLite for .NET.** Applies a tuned production pragma set — WAL journaling,
`synchronous=NORMAL`, `foreign_keys=ON`, a `busy_timeout`, a
shared `mmap_size`, and a capped `journal_size_limit` — to **every** SQLite connection, so your app
gets correct, concurrent, production-ready SQLite by default instead of the lock-prone stock config.

It also hardens the connection: `trusted_schema=OFF` so a schema cannot invoke arbitrary functions
from a view or index expression, `cell_size_check=ON` so a corrupt page is caught as it is read, and a
bounded `PRAGMA optimize` (`SqlitePragmas.Optimize`) to keep the query planner's statistics from going
stale — the usual reason a query that was instant in development crawls in production.

Standalone and lean: it depends only on `Microsoft.Data.Sqlite` and is **reflection-free**, so it is
fine under trimming/AOT and on mobile. You do **not** need the rest of Rask to use it.

## Install

```bash
dotnet add package Rask.SQLite
```

## Use

```csharp
builder.Services.AddRaskSqlite($"Data Source={dbPath}");

// then inject ISqlite:
await using var connection = await factory.CreateOpenAsync(ct);   // pragmas already applied
```

The production defaults are on out of the box; override any of them:

```csharp
builder.Services.AddRaskSqlite($"Data Source={dbPath}", p =>
{
    p.BusyTimeout = TimeSpan.FromSeconds(10);
    p.CacheSize = -20_000;              // negative ⇒ KiB, so 20 MB
    p.TempStore = SqliteTempStore.Memory;
});
```

## Concurrent writes: IMMEDIATE transactions + a non-blocking retry

For the write path under concurrency, `InImmediateTransactionAsync` runs your work in a
`BEGIN IMMEDIATE` transaction and acquires the write lock through a **non-blocking, fair-interval
retry** — a constant 1 ms poll that *yields the thread* while it waits (no blocked
thread, no spurious `database is locked`):

```csharp
await factory.InImmediateTransactionAsync(async (connection, ct) =>
{
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "INSERT INTO WriteLogs (Note) VALUES ($note);";
    cmd.Parameters.AddWithValue("$note", note);
    await cmd.ExecuteNonQueryAsync(ct);
});
```

Tune it with `AddRaskSqlite(cs, configureRetry: r => …)` (defaults: 5 s timeout, 1 ms interval).

Your callback **runs at least once, not exactly once**: SQLite can roll a transaction back on its own
when a contended `COMMIT` is answered with `SQLITE_BUSY`, and the whole transaction is then re-run
because everything the callback wrote went with it. Keep the callback re-runnable and put side effects
that must not repeat outside the transaction.

## Using Entity Framework Core?

Add [`Rask.SQLite.EntityFrameworkCore`](https://www.nuget.org/packages/Rask.SQLite.EntityFrameworkCore)
for the one-line `UseRaskSqlite(...)` (a drop-in for `UseSqlite` that wires the pragma interceptor).
It's a separate package so the pragma engine stays free of an EF Core dependency.

## Defaults

| Pragma | Default |
|---|---|
| `journal_mode` | `WAL` |
| `synchronous` | `NORMAL` |
| `foreign_keys` | `ON` |
| `busy_timeout` | `5000` ms |
| `cache_size` | `2000` pages |
| `mmap_size` | `134217728` (128 MiB) |
| `journal_size_limit` | `67108864` (64 MiB) |
| `temp_store` | unset (opt-in `MEMORY`) |

## Notes

- **Applied on every open, not once at startup.** `Microsoft.Data.Sqlite` pools connections and the
  per-connection pragmas don't persist, so they are re-applied each time a connection opens (a
  `StateChange` hook on the factory's connections; the EF Core package uses a `ConnectionOpened`
  interceptor). Only `journal_mode=WAL` persists in the database file header.
- **Fully overridable / opt-outable.** Set any option to `null` to leave that pragma at SQLite's own
  default.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md>
