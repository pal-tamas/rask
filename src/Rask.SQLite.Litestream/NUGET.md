# Rask.SQLite.Litestream

**Managed [Litestream](https://litestream.io) backup for SQLite, supervised from inside your app.**
Restores the database from its replica on startup (when the local file is missing) and continuously
streams the write-ahead log to S3 / GCS / Azure Blob / file storage for the life of the process — all
driven by a hosted background service, so there is no separate sidecar container to orchestrate.

Pairs with [`Rask.SQLite`](https://www.nuget.org/packages/Rask.SQLite), whose WAL default is exactly
what Litestream requires. Standalone otherwise: it depends only on the Microsoft.Extensions
hosting/DI abstractions and [CliWrap](https://github.com/Tyrrrz/CliWrap).

> The `litestream` binary is **downloaded at build time** and dropped next to your app (SHA-256-verified,
> cached, tiny package) — nothing to install. Opt out with `-p:RaskLitestreamDownload=false` and set your
> own `ExecutablePath`.

## Install

```bash
dotnet add package Rask.SQLite.Litestream
```

## Use

```csharp
var dbPath = "/data/app.db";   // local disk — see the notes on network filesystems

builder.Services.AddRaskSqliteLitestream(o =>
{
    o.DatabasePath = dbPath;
    o.ReplicaUrl = "s3://my-bucket/app";     // or gcs://, abs:// (Azure Blob), file:///backups/app
    // o.ExecutablePath = "/usr/local/bin/litestream";  // if it isn't on PATH
});

builder.Services.AddDbContextFactory<AppDb>(o => o.UseRaskSqlite($"Data Source={dbPath}"));

var app = builder.Build();

// Restore from the replica BEFORE opening the DB (no-op if the file already exists locally).
await app.Services.RestoreSqliteFromLitestreamAsync();

// ... EnsureCreated / migrate / seed, then app.Run();
```

Continuous replication then runs automatically as a hosted service until the app stops; on shutdown
it interrupts `litestream` and lets it flush (see `ShutdownGracePeriod`).

## Notes

- **Single writer only.** Litestream assumes one process writes the database. Run a **single
  instance** — do not scale out — or the replica diverges.
- **Local disk, not a network share.** SQLite WAL needs real filesystem locking; keep the database on
  local/ephemeral disk and let Litestream replicate to durable storage. This is the pattern Litestream
  was built for (ephemeral container + object-storage backup).
- **Resilient by design.** A backup failure is logged at `Critical` but never crashes the app.
- **Advanced config.** Point `ConfigPath` at a full `litestream.yml` for multiple databases or custom
  retention / sync intervals.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md>
