# Rask.SQLite.EntityFrameworkCore

**The Entity Framework Core integration for [`Rask.SQLite`](https://www.nuget.org/packages/Rask.SQLite).**
`UseRaskSqlite(...)` is a drop-in replacement for `UseSqlite` that also wires a `ConnectionOpened`
interceptor applying the production pragma set — WAL, `synchronous=NORMAL`,
`foreign_keys=ON`, a `busy_timeout`, `mmap_size`, `journal_size_limit` — to every connection the context
opens.

It also makes `decimal` correct. EF Core stores one as invariant TEXT and sorts it with a collating
sequence it registers as `decimal.Parse(x)` — with no `IFormatProvider`, so it reads that invariant text
under the machine's `CurrentCulture`. On `de-DE` an `ORDER BY` silently mis-sorts (`"19.95"` parses as
`1995`); on `en-HU` it throws inside a native callback that cannot be unwound and **takes the process
down**. `UseRaskSqlite` re-registers the collation invariantly on every open, changing nothing in the
database file — no column type, no DDL, no migration.

Split out from `Rask.SQLite` so apps that only use the raw `Microsoft.Data.Sqlite` path (or run on
mobile / under AOT, where you don't want EF Core) can stay lean.

## Install

```bash
dotnet add package Rask.SQLite.EntityFrameworkCore
```

## Use

```csharp
builder.Services.AddDbContextFactory<AppDb>(o =>
    o.UseRaskSqlite($"Data Source={dbPath}"));
```

Override any pragma via the optional configure delegate:

```csharp
o.UseRaskSqlite($"Data Source={dbPath}", p =>
{
    p.BusyTimeout = TimeSpan.FromSeconds(10);
    p.CacheSize = -20_000;   // negative ⇒ KiB, so 20 MB
});
```

## Busy-retry for `SaveChanges`

Pass `configureRetry` (even empty) to register a fair-interval execution strategy so
`SaveChanges`/queries retry on `SQLITE_BUSY`/`SQLITE_LOCKED` at a constant 1 ms interval, awaiting
(not blocking) between attempts:

```csharp
o.UseRaskSqlite($"Data Source={dbPath}", configureRetry: _ => { });
```

The truly non-blocking, `BEGIN IMMEDIATE` write path lives in `Rask.SQLite`
(`ExecuteInImmediateTransactionAsync`); see the docs for when to reach for it.

Not using EF Core? Use `Rask.SQLite` directly: `services.AddRaskSqlite(cs)` + inject
`IRaskSqliteConnectionFactory`.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md>
