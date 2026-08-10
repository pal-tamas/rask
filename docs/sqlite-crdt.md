# Multi-writer SQLite (`Rask.SQLite.Crdt`)

> **In practice:** several replicas of one database, written independently and merged without
> conflicts — while the application code stays ordinary EF Core.

```bash
dotnet add package Rask.SQLite.Crdt
```

```csharp
options.UseSqlite($"Data Source={file};Pooling=False")
       .UseRaskCrdt(o => o.ExtensionPath = crsqlitePath);

protected override void OnModelCreating(ModelBuilder b) => b.ApplyCrdtConventions();

await context.PromoteToCrrsAsync();   // once the schema exists — see "Creating the schema" below

var feed    = new CrdtChangeFeed(context);
var changes = await feed.ReadChangesAsync(sinceDbVersion: watermark);
await feed.ApplyChangesAsync(theirChanges);
```

This wraps [cr-sqlite](https://github.com/vlcn-io/cr-sqlite), a SQLite extension that turns tables into
*conflict-free replicated relations*. Rask's part is the EF Core integration: the extension has several
requirements EF violates by default, and each one fails in a way that points somewhere else.

## Merging is per column, not per row

The unit of replication is **one column of one row**, stamped with which replica set it and when:

| | Alice | Bob | after merging |
|---|---|---|---|
| `Title` | `"final"` | *untouched* | `"final"` |
| `Priority` | *untouched* | `9` | `9` |

Two devices editing different fields of the same record both keep their work. Last-writer-wins applies
only when two devices write the **same** field — the case where something genuinely has to be chosen.
That is the whole reason to reach for a CRDT rather than a `LastModified` column.

Applying a change twice is a no-op. That is what makes it safe to re-send after an upload whose outcome
is unknown, and it is why a replica never has to track what its peers already hold.

## Transport is deliberately absent

`CrdtChangeFeed` hands you an ordered log and takes one back. Where those bytes travel is the app's
business — an object-storage bucket, a socket, a file on a USB stick — and keeping it that way is what
lets this work with no server at all. For the bucket case, pair it with
[`Rask.Sync.Client`](sync-client.md).

```csharp
var mine = await feed.ReadChangesAsync(sinceDbVersion: theirWatermark);
// ... send `mine` however you like, receive `theirs` ...
await feed.ApplyChangesAsync(theirs);
```

Reading from a watermark is what makes a sync cost what *changed* rather than what *exists*.
`GetDbVersionAsync()` is the high-water mark to remember; `GetSiteIdAsync()` is this replica's identity.

## The three things that fail quietly without this package

### The extension is per connection, not per process

`Microsoft.Data.Sqlite` pools connections, and a reused handle is a fresh open as far as extensions are
concerned. Loading once at startup therefore works until the pool recycles and then silently stops.
`UseRaskCrdt` loads it on every open and calls `crsql_finalize()` before every close.

**Use `Pooling=False`.** cr-sqlite keeps per-connection state, and a handle returned to the pool
mid-state and handed to somebody else corrupts quietly rather than failing.

### Every required column needs a SQL default

cr-sqlite refuses a `NOT NULL` column that has no default, and the requirement is not arbitrary: a peer
still running an older schema has to be able to apply a change that says nothing about a column it has
never heard of, and a default is what lets it. EF emits exactly that shape for every required property,
so a perfectly ordinary model is rejected outright.

`ApplyCrdtConventions()` gives every non-key, non-nullable column a default. It sets a default
*expression* rather than a value on purpose: EF suppresses a default equal to the CLR default — it
cannot tell "unset" from "set to `false`" — so a `bool` column would otherwise come out bare, and only
that one column would fail, which reads like a cr-sqlite bug rather than an EF one.

Columns you have already given a default keep it. Nullable columns are left alone, because `NULL` is
already an applicable value.

### Creating the schema: order matters

Loading cr-sqlite seeds its own bookkeeping tables, and `EnsureCreated` treats a database that already
has tables as provisioned. Creating the schema through a context that loads the extension therefore
creates **nothing at all**, and the first sign of trouble is `PromoteToCrrsAsync` complaining that a
table has no primary key.

```csharp
// 1. Schema on a context WITHOUT the extension.
await using (var plain = new AppContext(plainOptions))
{
    await plain.Database.EnsureCreatedAsync();   // or MigrateAsync()
}

// 2. Promote on a context WITH it.
await using var context = new AppContext(crdtOptions);
await context.PromoteToCrrsAsync();
```

`PromoteToCrrsAsync()` promotes every table in the model. Name a subset through
`RaskCrdtOptions.Tables` when only part of the database is shared.

## The native binary is yours to supply

`ExtensionPath` points at cr-sqlite's loadable extension — `crsqlite.dylib`, `.so` or `.dll`. It is not
bundled, because cr-sqlite ships a separate binary per platform and which one is right depends on where
the app runs rather than on which package it referenced. Download it from
[cr-sqlite's releases](https://github.com/vlcn-io/cr-sqlite/releases) and deploy it alongside the app.

Configuring a non-SQLite provider is reported rather than skipped: silently doing nothing would leave an
app that looks like it works and never replicates, which surfaces later as data loss.

## Scope and limits

- **Per-field last-writer-wins**, not concurrent-edit merging within a field. Two people typing into the
  same text box at the same time still lose one of the two versions.
- **Every replica holds the whole database.** There is no partial replication and no per-row
  authorization, so this targets one user across many devices, and small trusted teams.
- **Promotion is one-way in practice.** Treat `crsql_as_crr` as part of the schema, applied wherever
  migrations are.
- Tables need a primary key that is not `rowid` alone — cr-sqlite identifies rows by their key across
  replicas, and an autoincrementing integer means something different on each device. Prefer a `Guid`.

## Testing

The merge behaviour is covered by tests that run against the real extension and skip when it is absent:

```bash
RASK_CRSQLITE_PATH=/path/to/crsqlite.dylib dotnet test tests/Rask.SQLite.Crdt.Tests
```

Everything reachable without the native binary — the conventions, the options, the connection
lifecycle — is covered by tests that always run.

## See also

- [Syncing between devices](sync-client.md) — moving the change feed over a bucket with no server.
- [Offline-first merge](sync.md) — the pure-logic merge engine, for apps not backed by SQLite.
- [SQLite production pragmas](sqlite.md) — WAL and friends on every connection.
