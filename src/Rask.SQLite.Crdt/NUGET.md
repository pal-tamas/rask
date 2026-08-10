# Rask.SQLite.Crdt

Several replicas of one SQLite database, written independently and merged without conflicts — through
ordinary EF Core. Wires the [cr-sqlite](https://github.com/vlcn-io/cr-sqlite) extension into a
`DbContext` so application code stays LINQ, change tracking and `SaveChanges`.

```bash
dotnet add package Rask.SQLite.Crdt
```

## Use it

```csharp
options.UseSqlite($"Data Source={file};Pooling=False")
       .UseRaskCrdt(o => o.ExtensionPath = crsqlitePath);

protected override void OnModelCreating(ModelBuilder b) => b.ApplyCrdtConventions();

// After the schema exists, and on a context that does NOT load the extension. See below.
await context.PromoteToCrrsAsync();

// Ship what changed since a peer last heard from you; take back what they have.
var feed    = new CrdtChangeFeed(context);
var changes = await feed.ReadChangesAsync(sinceDbVersion: watermark);
await feed.ApplyChangesAsync(theirChanges);
```

Transport is deliberately absent. `ReadChangesAsync` hands you an ordered log and `ApplyChangesAsync`
takes one back; whether those bytes travel over a bucket, a socket or a USB stick is the app's business,
and keeping it that way is what lets this work with no server at all. Pair it with
[`Rask.Sync.Client`](https://www.nuget.org/packages/Rask.Sync.Client) for the bucket case.

## Merging is per column, not per row

Two devices editing *different fields of the same record* both keep their work — the unit of
replication is one column of one row, stamped with which replica set it and when. Last-writer-wins
applies only when two devices write the **same** field, which is the case where something genuinely has
to be chosen.

Applying a change twice is a no-op, so re-sending after an upload whose outcome is unknown is safe, and
a replica never has to track what its peers already hold.

## The three things that fail quietly without it

**The extension is per connection, not per process.** `Microsoft.Data.Sqlite` pools connections, so
loading once at startup works until the pool recycles and then silently stops. It is loaded on every
open and finalized before every close. Use `Pooling=False`: cr-sqlite keeps per-connection state, and a
handle returned to the pool mid-state and reused elsewhere corrupts quietly rather than failing.

**Every required column needs a SQL default.** cr-sqlite refuses a `NOT NULL` column without one,
because a peer still running an older schema has to be able to apply a change that never mentions it.
EF emits exactly that shape for every required property, so an ordinary model is rejected outright.
`ApplyCrdtConventions()` supplies the defaults — as expressions, because EF drops a default equal to the
CLR default and a single `bool` column would otherwise come out bare.

**Order matters when creating the schema.** Loading cr-sqlite seeds its own bookkeeping tables, and
`EnsureCreated` treats a database that already has tables as provisioned — so creating the schema
through a context that loads the extension creates *nothing at all*, and the first sign of trouble is
the promotion complaining that a table has no primary key. Create the schema on a context without the
extension, then promote.

## The native binary is yours to supply

`ExtensionPath` points at cr-sqlite's loadable extension (`crsqlite.dylib` / `.so` / `.dll`). It is not
bundled: cr-sqlite ships a separate binary per platform, and which one is right depends on where the app
runs rather than on which package it referenced.

## Scope

Per-field last-writer-wins, not concurrent-edit merging within a field. Every replica holds the whole
database, so this targets **one user across many devices, and small trusted teams** — there is no
partial replication and no per-row authorization.

---

Part of [Rask](https://github.com/pal-tamas/rask).
