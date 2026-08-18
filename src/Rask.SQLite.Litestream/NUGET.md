# Rask.SQLite.Litestream

**Managed [Litestream](https://litestream.io) backup for SQLite, supervised from inside your app.**
Restores the database from its replica on startup (when the local file is missing) and continuously
streams the write-ahead log to S3 / GCS / Azure Blob / file storage for the life of the process — all
driven by a hosted background service, so there is no separate sidecar container to orchestrate.

It also **verifies that what has been replicated is restorable** — on an opt-in schedule, by restoring
the replica back out to a throwaway path and checking a sentinel survived the round trip. "The
replicator is running" and "the backup can be restored" are different facts, and only one of them is
usually discovered at the moment you need it.

Builds on [`Rask.SQLite`](https://www.nuget.org/packages/Rask.SQLite), whose WAL default is exactly
what Litestream requires and whose non-blocking busy-retry writes the verification sentinel, plus
[CliWrap](https://github.com/Tyrrrz/CliWrap) to drive the binary.

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
- **Checkable.** A log line tells you when replication broke; nothing tells you it's healthy. Resolve the
  `LitestreamStatus` singleton for `IsReplicating`, `LastStartedAt`/`LastExitedAt`, `RestartCount`,
  `LastExitCode` and `LastError`. A clean shutdown clears `IsReplicating` without counting a restart, so a
  climbing `RestartCount` means backups are flapping.
- **Restorable, not just running.** Every one of those fields describes the local child process. A replica
  writing to the wrong prefix, or a bucket whose credentials were rotated to read-only, keeps
  `IsReplicating` true right up until the restore. Turn on `Verification` to prove the round trip:

  ```csharp
  o.Verification.Enabled = true;              // off by default — see the cost note below
  o.Verification.Interval = TimeSpan.FromHours(24);
  ```

  Each pass writes a sentinel row, waits for replication to carry it, restores to a temp path and checks
  it came back, then publishes `LitestreamStatus.Verification`: `Outcome`, `LastVerifiedAt`,
  `ReplicationLag` and `LastError`. **Alert on a `LastVerifiedAt` that stops moving** — an `Inconclusive`
  pass just means the sentinel had not shipped yet, which is lag, not a broken backup.

  **This costs money.** A verification pass is a real restore and a real download, so it is off by
  default, defaults to daily when on, and belongs nowhere near a health-check endpoint that anything can
  poll. `ISqliteBackupVerifier` is registered either way if you want to trigger one by hand.
- **Advanced config.** Point `ConfigPath` at a full `litestream.yml` for multiple databases or custom
  retention / sync intervals.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md>
