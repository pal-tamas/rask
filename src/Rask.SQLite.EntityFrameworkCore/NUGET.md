# Rask.SQLite.EntityFrameworkCore

**The Entity Framework Core integration for [`Rask.SQLite`](https://www.nuget.org/packages/Rask.SQLite).**
`UseRaskSqlite(...)` is a drop-in replacement for `UseSqlite` that also wires a `ConnectionOpened`
interceptor applying the Rails-style production pragma set — WAL, `synchronous=NORMAL`,
`foreign_keys=ON`, a `busy_timeout`, `mmap_size`, `journal_size_limit` — to every connection the context
opens.

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

Not using EF Core? Use `Rask.SQLite` directly: `services.AddRaskSqlite(cs)` + inject
`IRaskSqliteConnectionFactory`.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md>
