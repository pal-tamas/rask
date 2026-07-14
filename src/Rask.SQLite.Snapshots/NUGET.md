# Rask.SQLite.Snapshots

**Scheduled, consistent SQLite backups — no external binary.** Takes point-in-time file snapshots of a
live SQLite database on a schedule using SQLite's **Online Backup API** (never an unsafe `File.Copy` of
a WAL database), keeps the newest N, and writes them to a directory — or a pluggable store for object
storage.

Use it on its own for simple periodic full backups without a sidecar or object storage, or alongside
[`Rask.SQLite.Litestream`](https://www.nuget.org/packages/Rask.SQLite.Litestream) (continuous streaming)
as a second line of defence. Standalone: depends only on `Microsoft.Data.Sqlite` and the
Microsoft.Extensions hosting/DI abstractions.

## Install

```bash
dotnet add package Rask.SQLite.Snapshots
```

## Use

```csharp
builder.Services.AddRaskSqliteSnapshots(o =>
{
    o.DatabasePath = "/data/app.db";
    o.DestinationDirectory = "/backups";
    o.Interval = TimeSpan.FromHours(6);
    o.Retain = 14;                  // keep the 14 newest
    o.SnapshotOnStartup = true;     // also take one at boot
});
```

Snapshots land as `app-20260714-030000000.db` — each a complete, standalone SQLite database you can
open, copy or archive.

Need a backup on demand (say, before a risky migration)? Inject `ISqliteSnapshotter`:

```csharp
var name = await snapshotter.SnapshotAsync(ct);
```

Sending snapshots to object storage instead of a directory? Register your own `ISqliteSnapshotStore`
before `AddRaskSqliteSnapshots` and `DestinationDirectory` is no longer required.

## Notes

- **Consistent, not a raw copy.** The Online Backup API copies pages while writers continue, so the
  snapshot is a valid database even under load — unlike copying the file yourself.
- **Resilient.** A failed snapshot is logged and the schedule continues; a backup problem never crashes
  the app.
- **Dedicated directory.** Point `DestinationDirectory` at a directory used only for snapshots — pruning
  is scoped to this database's own files, but a dedicated directory keeps things tidy.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/sqlite.md#scheduled-snapshots>
