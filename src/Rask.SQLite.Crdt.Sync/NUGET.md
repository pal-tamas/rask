# Rask.SQLite.Crdt.Sync

Shares a CRDT SQLite database between devices through an object-storage bucket, with **no server in
between**. Ships [`Rask.SQLite.Crdt`](https://www.nuget.org/packages/Rask.SQLite.Crdt)'s change feed over
[`Rask.ObjectStore`](https://www.nuget.org/packages/Rask.ObjectStore).

```bash
dotnet add package Rask.SQLite.Crdt.Sync
```

## Use it

```csharp
var engine = new CrdtSyncEngine(objectStore, new CrdtChangeFeed(context));

engine.Changed += status => Render(status);   // synced / offline, published, received, peers
await engine.SyncAsync();                     // publish mine, then apply everyone else's
```

Local edits are ordinary EF Core — `SaveChanges` and nothing else. Syncing is a separate act, and the
app behaves identically whether or not it ever succeeds.

## How it avoids needing a server

**Each device writes only under its own prefix** — `crdt/{site-id}/changes/` — and never touches
another's. No two devices ever write the same key, so there is nothing to lock, nothing to retry on
conflict, and no lease to renew or leak if a device disappears mid-write. Everything else follows.

**Reading is forward-only.** Keys carry the publishing replica's own `db_version` range in fixed-width
hex, so they sort in the order changes were made and a remembered key resumes exactly where the last
sync stopped. Peers are found with a grouped listing, so discovery costs one response listing the
*devices* rather than one listing every object they have ever written.

**Only your own work is published.** A replica's feed carries every change it has ever accepted, so
publishing it unfiltered would have each device re-uploading every other device's history. Uploads are
batched, because object storage charges per request.

## The bucket doesn't grow forever

A replica folds its own objects into one holding its whole current contribution and removes the rest —
automatically past `CompactAfterObjects` (default 50), or via `await engine.CompactAsync()`.

Cheap because the change feed is **current state, not history**: one entry per (row, column) with the
value that won, so editing a field forty times leaves one entry and a deleted row collapses to a single
tombstone. No coordination is needed, since a replica only rewrites its own prefix. The payoff is a new
device's first sync — one object per peer, instead of replaying every sync those peers ever did.

## Offline is the normal case, not an error

The **database is the queue**. An edit is committed to SQLite before any sync is attempted, so there is
nothing to lose if the bucket is unreachable and no "offline mode" to enter. A failed sync leaves the
database untouched and the next one publishes the same changes — safe precisely because applying a
change twice does nothing.

`CrdtSyncPhase.Offline` is deliberately not a failure state. Showing it as an error trains people to
ignore the indicator that matters.

## The sync state is a cache, not a record

`ICrdtSyncStore` holds the peer watermarks and what this replica last published. Losing all of it costs
re-uploading and re-reading — **never data** — because SQLite already holds the truth. That is a
materially better bargain than a queue-based sync, where losing the queue loses a user's offline edits,
and it is why `InMemoryCrdtSyncStore` is a legitimate default rather than a test double. A fresh state
is answered from the bucket rather than assumed to mean "never published", so a reinstalled device does
not re-upload its history.

## Scope

Every device holds bucket credentials and the whole database, so this targets **one user across many
devices, and small trusted teams** — not multi-tenant apps. There is no partial replication and no
per-row authorization.

There is no conflict count in the status, on purpose: merging is per column and automatic, so nothing is
silently discarded and there is nothing a user could be asked to resolve.

---

Part of [Rask](https://github.com/pal-tamas/rask).
