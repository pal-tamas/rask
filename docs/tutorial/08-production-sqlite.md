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

You already have this. `RaskApp` opens the database with `UseRaskSqlite` rather than `UseSqlite`, because
`Rask:Database:Provider` is `sqlite` unless you say otherwise. The file is `Rask:ConnectionStrings:App` in
`appsettings.json` — `Data Source=app.db` while you develop — and there is nothing about it in `Program.cs`.

`UseRaskSqlite` is a drop-in for `UseSqlite` that also installs the pragma interceptor, so every background
processor (jobs, mail, outbox), every page, and every `Product.Read.Where(…)` and every command handler shares a
connection that won't spuriously fail under load. `StrictTables` makes SQLite enforce each column's declared
type rather than quietly storing the text `"lots"` in an `INTEGER` column — see
[STRICT tables](../sqlite.md#strict-tables--making-the-store-enforce-your-types). An app that writes its own
context gets the same by registering it with `UseRaskDatabase(sp)` ([Rask.Data](../data.md#choosing-the-database)).

See [production SQLite](../sqlite.md) for the full pragma table, the load-test numbers, and the
non-blocking write-retry story.

## 2. Snapshots — the cheap half

Scheduled point-in-time backups are a battery, on like the rest, and tuned in `appsettings.json` beside the
connection string:

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
continuous backup is for: [Litestream](https://litestream.io), run as a managed background service that
**streams every change off the box** to object storage (S3, GCS, Azure Blob, or a file target). It is off
until `Rask:Litestream:ReplicaUrl` names a replica — the scaffolded `appsettings.json` leaves it empty — so
`rask dev` works on a laptop with no `litestream` binary and no cloud credentials.

Set it, and `RaskApp` does two things:

- **It restores first**, before anything opens the database — a no-op when `app.db` is already there. Restore
  is skipped once the file exists, so doing it any later means a fresh machine quietly starts with an empty
  database instead of your data. This is the ordering that is easy to get wrong by hand.
- **It replicates** the database behind `Rask:ConnectionStrings:App`, reading the rest of `Rask:Litestream`
  for its settings.

The csproj sets `RaskLitestreamDownload=false`: the binary belongs in the Docker image, which the scaffolded
`Dockerfile` copies it into, rather than being fetched during everyone's build.

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
