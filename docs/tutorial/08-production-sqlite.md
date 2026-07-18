# Chapter 8 — Production SQLite

> **Goal:** make the one SQLite file safe to run in production — correct under concurrency, and backed up
> off the box.
> **You'll add:** `UseRaskSqlite(…)` and `AddRaskSqliteLitestream(…)`.

Everything so far — products, orders, jobs, mail, cache, outbox — lives in a single `app.db`. That's the
One-Person-Framework bet: **SQLite is your production database.** Two things make that safe: the right
connection pragmas, and continuous backup.

## 1. Swap in the production pragmas

SQLite's defaults are tuned for a single embedded process — no WAL, no `busy_timeout`, foreign keys off —
so concurrent web requests hit `database is locked`. `Rask.SQLite` applies a tuned pragma set (WAL, a
busy-timeout, `foreign_keys=ON`, and more) to **every** connection. Add the EF Core package and change one
word:

```bash
dotnet add package Rask.SQLite.EntityFrameworkCore
```

In `Program.cs`, swap `UseSqlite` for `UseRaskSqlite` (a drop-in that also installs the pragma interceptor):

```csharp
using Rask.SQLite;

builder.Services.AddDbContextFactory<ProductsDbContext>((sp, o) => o
    .UseRaskSqlite("Data Source=app.db")                    // ← was .UseSqlite(...)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

That's the whole change — every background processor (jobs, mail, outbox) and every page now shares a
connection that won't spuriously fail under load. See [production SQLite](../sqlite.md) for the full pragma
table, the load-test numbers, and the non-blocking write-retry story.

## 2. Back it up continuously with Litestream

A database is only as safe as its backups. `Rask.SQLite.Litestream` runs [Litestream](https://litestream.io)
as a managed background service that **streams every change off the box** to object storage (S3, GCS, Azure
Blob, or a file target), and can restore on startup:

```bash
dotnet add package Rask.SQLite.Litestream
```

```csharp
builder.Services.AddRaskSqliteLitestream(o =>
{
    o.DatabasePath = "app.db";
    o.ReplicaUrl   = "s3://my-bucket/shop";   // or gcs://, abs://, file:///…
    // o.RestoreOnStartup = true;             // default: restore from the replica if app.db is missing
});

var app = builder.Build();

// Restore from the replica before the DB is opened (no-op if app.db already exists).
await app.Services.RestoreSqliteFromLitestreamAsync();

app.Run();
```

Now the box is **disposable**: if it dies, a fresh box restores `app.db` from the replica on startup and
keeps going. That's what makes "one server" safe rather than scary — durability doesn't depend on that one
machine. (Prefer scheduled full-file snapshots instead of/alongside streaming? `Rask.SQLite.Snapshots` does
that with no external binary — see [production SQLite](../sqlite.md#scheduled-snapshots).)

## Verify

- The app still builds and runs after the `UseRaskSqlite` swap; under concurrent writes you no longer see
  `database is locked` (a WAL `app.db-wal` file appears next to `app.db`).
- With Litestream configured against a real bucket, deleting `app.db` and restarting restores it from the
  replica (watch the startup log).

**Learn more:** [production SQLite](../sqlite.md) · [Rask.Data](../data.md)

Next → **[Chapter 9: Deploy to one box](09-deploy.md)**
