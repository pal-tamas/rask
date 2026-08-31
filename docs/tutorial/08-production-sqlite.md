# Chapter 8 — Production SQLite

> **Goal:** make the one SQLite file safe to run in production — correct under concurrency, and backed up
> off the box.
> **You'll have run:** `rask new Shop` — snapshots and continuous backup are both standard

Everything so far — products, orders, jobs, mail, cache, outbox — lives in a single `app.db`. That's the
One Person Framework bet: **SQLite is your production database.** Two things make that safe: the right
connection pragmas, and backups that don't live on the same machine.

## 1. The production pragmas

SQLite's defaults are tuned for a single embedded process — no WAL, no `busy_timeout`, foreign keys off —
so concurrent web requests hit `database is locked`. `Rask.SQLite` applies a tuned pragma set (WAL, a
busy-timeout, `foreign_keys=ON`, and more) to **every** connection.

You already have this. `rask new` writes `UseRaskSqlite` rather than `UseSqlite`:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(builder.Configuration.GetConnectionString("App") ?? "Data Source=app.db")
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

It is a drop-in for `UseSqlite` that also installs the pragma interceptor — one word, and every background
processor (jobs, mail, outbox) and every page shares a connection that won't spuriously fail under load.
Retrofitting an existing app is the same one-word change — the package is already there, since `Rask`
brings it.

See [production SQLite](../sqlite.md) for the full pragma table, the load-test numbers, and the
non-blocking write-retry story.

## 2. Snapshots — the cheap half

`--snapshots` wires scheduled point-in-time backups:

```csharp
builder.Services.AddRaskSqliteSnapshots(o =>
{
    o.DatabasePath = builder.Configuration["Sqlite:Path"] ?? "app.db";
    o.DestinationDirectory = builder.Configuration["Sqlite:SnapshotDirectory"] ?? "snapshots";
    o.Interval = TimeSpan.FromHours(6);
    o.Retain = 7;
});
```

These go through SQLite's **Online Backup API**, not a file copy. That distinction is the whole value: with
WAL on, `cp app.db backup.db` can capture a torn database, because the committed data you want is split
between the file and the `-wal`. The backup API reads through a connection and gets a consistent image of a
live database. No external binary, no credentials.

## 3. Litestream — the off-box half

Snapshots on the same disk protect you from a bad migration, not from losing the disk. That's what
continuous backup is for, and `--data` already wired it in Chapter 1 — `Rask.SQLite.Litestream` runs
[Litestream](https://litestream.io) as a managed background service that **streams every change off the
box** to object storage (S3, GCS, Azure Blob, or a file target):

```csharp
var replicaUrl = builder.Configuration["Litestream:ReplicaUrl"];
if (!string.IsNullOrWhiteSpace(replicaUrl))
{
    builder.Services.AddRaskSqliteLitestream(o =>
    {
        o.DatabasePath = builder.Configuration["Sqlite:Path"] ?? "app.db";
        o.ReplicaUrl = replicaUrl;
    });
}

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(replicaUrl))
{
    // Restore BEFORE anything opens the database — a no-op when app.db is already there.
    await app.Services.RestoreSqliteFromLitestreamAsync();
}
```

Two details the scaffold gets right and are easy to get wrong by hand:

- **The restore runs first**, before the schema is created or any pillar's processor starts. Restore is
  skipped once the file exists, so putting it later means a fresh machine quietly starts with an empty
  database instead of your data.
- **Both halves are gated on the same config.** Litestream stays off until you set a replica URL, so
  `dotnet run` works on a laptop with no `litestream` binary and no cloud credentials. (The restore call
  throws when Litestream was never registered — useful for a real wiring mistake, fatal for a fresh
  scaffold, hence the guard.) The csproj also sets `RaskLitestreamDownload=false`: the binary belongs in
  the Docker image, which `--docker` copies it into, rather than being fetched during everyone's build.

Set the replica when you deploy:

```bash
Litestream__ReplicaUrl=s3://my-bucket/shop
```

Now the box is **disposable**: if it dies, a fresh box restores `app.db` from the replica on startup and
keeps going. That's what makes "one server" safe rather than scary — durability doesn't depend on that one
machine.

## Verify

- A WAL file (`app.db-wal`) appears next to `app.db`, and under concurrent writes you no longer see
  `database is locked`.
- A `.db` file lands in the snapshot directory on the configured interval.
- With Litestream configured against a real bucket, deleting `app.db` and restarting restores it from the
  replica (watch the startup log).
- **See it running:** [`samples/Rask.Example.Shop`](../../samples/Rask.Example.Shop)'s `/ops` page reads
  `journal_mode` and `foreign_keys` back from the live connection and counts the snapshots on disk.

**Learn more:** [production SQLite](../sqlite.md) · [Rask.Data](../data.md)

Next → **[Chapter 9: Push notifications](09-web-push.md)**
