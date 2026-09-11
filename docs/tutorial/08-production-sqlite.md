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
    .UseRaskSqlite(sp)
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

There is no connection string in `Program.cs`: `UseRaskSqlite` reads `Rask:ConnectionStrings:App` from
`appsettings.json` — `Data Source=app.db` while you develop — which is why it takes the service provider.

It is a drop-in for `UseSqlite` that also installs the pragma interceptor — one word, and every background
processor (jobs, mail, outbox), every page, and every `Product.Where(…)` or `Product.CreateAsync(…)` shares a
connection that won't spuriously fail under load. `StrictTables` makes SQLite enforce each column's declared
type rather than quietly storing the text `"lots"` in an `INTEGER` column — see
[STRICT tables](../sqlite.md#strict-tables--making-the-store-enforce-your-types). Retrofitting an existing app
is the same one-word change — the package is already there, since `Rask` brings it.

See [production SQLite](../sqlite.md) for the full pragma table, the load-test numbers, and the
non-blocking write-retry story.

## 2. Snapshots — the cheap half

`rask new` wires scheduled point-in-time backups:

```csharp
builder.Services.AddRaskSqliteSnapshots();
```

…tuned in `appsettings.json`, beside the connection string:

```jsonc
"Rask": {
  "Snapshots": {
    "DestinationDirectory": "snapshots",
    "Interval": "06:00:00",
    "Retain": 7
  }
}
```

It snapshots the database behind `Rask:ConnectionStrings:App`, so there is no second path to keep in step
with the first.

These go through SQLite's **Online Backup API**, not a file copy. That distinction is the whole value: with
WAL on, `cp app.db backup.db` can capture a torn database, because the committed data you want is split
between the file and the `-wal`. The backup API reads through a connection and gets a consistent image of a
live database. No external binary, no credentials.

## 3. Litestream — the off-box half

Snapshots on the same disk protect you from a bad migration, not from losing the disk. That's what
continuous backup is for, and `rask new` already wired it in Chapter 1 — `Rask.SQLite.Litestream` runs
[Litestream](https://litestream.io) as a managed background service that **streams every change off the
box** to object storage (S3, GCS, Azure Blob, or a file target):

```csharp
var replicaUrl = builder.Configuration["Rask:Litestream:ReplicaUrl"];
if (!string.IsNullOrWhiteSpace(replicaUrl))
{
    builder.Services.AddRaskSqliteLitestream();
}

var app = builder.Build();

// (Db.Configure and the first middleware sit here — nothing that touches the database.)

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
- **Both halves are gated on the same key.** `AddRaskSqliteLitestream` reads the rest of `Rask:Litestream`
  itself, and replicates the database behind `Rask:ConnectionStrings:App`; the scaffolded
  `appsettings.json` leaves `ReplicaUrl` empty. Litestream stays off until you set a replica URL, so
  `dotnet run` works on a laptop with no `litestream` binary and no cloud credentials. (The restore call
  throws when Litestream was never registered — useful for a real wiring mistake, fatal for a fresh
  scaffold, hence the guard.) The csproj also sets `RaskLitestreamDownload=false`: the binary belongs in
  the Docker image, which the scaffolded `Dockerfile` copies it into, rather than being fetched during
  everyone's build.

Set the replica when you deploy:

```bash
rask deploy --env "Rask__Litestream__ReplicaUrl=s3://my-bucket/shop"
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
- Your `/ops` page (chapter 10) reads `journal_mode` and `foreign_keys` back from the live connection and
  counts the snapshots on disk.

**Learn more:** [production SQLite](../sqlite.md) · [Rask.Data](../data.md)

Next → **[Chapter 9: Push notifications](09-web-push.md)**
