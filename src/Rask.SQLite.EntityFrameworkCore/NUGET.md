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
builder.Services.AddDbContextFactory<AppDb>((sp, o) => o.UseRaskSqlite(sp));
```

The connection string is `Rask:ConnectionStrings:App` in configuration — which is why `UseRaskSqlite` takes
the service provider — and a missing one is an error naming the key to set:

```jsonc
// appsettings.json
{ "Rask": { "ConnectionStrings": { "App": "Data Source=app.db" } } }
```

Override any pragma in the `Rask:Sqlite` section, or via the optional configure delegate, which runs after
the section and wins:

```csharp
o.UseRaskSqlite(sp, p =>
{
    p.BusyTimeout = TimeSpan.FromSeconds(10);
    p.CacheSize = -20_000;   // negative ⇒ KiB, so 20 MB
});
```

## STRICT tables

SQLite is dynamically typed: the text `"lots"` stores happily in an `INTEGER` column and surfaces as a
cast error much later. Set `Rask:Sqlite:StrictTables` to `true` (or pass `o => o.StrictTables = true`) and
tables are created as [STRICT tables](https://sqlite.org/stricttables.html), so the store rejects the write
instead:

```jsonc
{ "Rask": { "Sqlite": { "StrictTables": true } } }
```

Every column must then declare one of `INT`, `INTEGER`, `REAL`, `TEXT`, `BLOB` or `ANY` — EF Core's
defaults all qualify, so a normal model needs no changes, and an explicit `HasColumnType(...)` outside
that set is reported against the table and column it came from. Strictness is decided when a table is
created, so this needs no migration and affects tables created from then on; `rask new` scaffolds it on,
where it is free.

## Busy-retry for `SaveChanges`

Set `Retry.Enabled` — `Rask:Sqlite:Retry:Enabled` in configuration, or in code — to register a fair-interval
execution strategy so `SaveChanges`/queries retry on `SQLITE_BUSY`/`SQLITE_LOCKED` at a constant 1 ms
interval, awaiting (not blocking) between attempts:

```csharp
o.UseRaskSqlite(sp, o => o.Retry.Enabled = true);
```

The truly non-blocking, `BEGIN IMMEDIATE` write path lives in `Rask.SQLite`
(`InImmediateTransactionAsync`); see the docs for when to reach for it.

## Non-overlapping ranges

`UseRaskSqlite` also enforces `Rask.Data`'s `HasNonOverlappingRange(...)`: migrations emit an index plus a
`BEFORE INSERT`/`BEFORE UPDATE` trigger pair, re-emitted after the table rebuilds SQLite needs for most
`ALTER`s (which would otherwise drop them). A violating save throws `RangeOverlapException` naming the
table. Both are inert until an entity declares a rule. `Rask.Data`'s `AddRaskData<TContext>()` refuses to
boot a context that declares a rule its provider would ignore; `UseRaskSqlite` is what satisfies it, and a
plain `UseSqlite` does not.

Not using EF Core? Use `Rask.SQLite` directly: `services.AddRaskSqlite()` + inject
`ISqlite`.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md>
