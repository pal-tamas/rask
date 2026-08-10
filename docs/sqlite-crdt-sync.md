# Sharing a CRDT database through a bucket (`Rask.SQLite.Crdt.Sync`)

> **In practice:** the piece that lets several devices share one SQLite database with **no server
> between them**. Ships [the change feed](sqlite-crdt.md) over [a bucket](object-storage.md).

```bash
dotnet add package Rask.SQLite.Crdt.Sync
```

```csharp
var engine = new CrdtSyncEngine(objectStore, new CrdtChangeFeed(context));

engine.Changed += status => Render(status);
await engine.SyncAsync();
```

Local edits stay ordinary EF Core. Syncing is a separate act, and the app behaves identically whether or
not it ever succeeds.

## The layout, and why it needs no coordination

```
crdt/
  <site-a>/changes/{fromVersion}__{toVersion}.json
  <site-b>/changes/{fromVersion}__{toVersion}.json
```

**Each device writes only under its own prefix and never touches another's.** No two devices ever write
the same key, so there is nothing to lock, nothing to retry on conflict, and no lease to renew or to
leak when a device disappears mid-write. Every other property below follows from that one rule.

The `site-id` is cr-sqlite's, so it is the same identity that stamps the changes themselves — a device
cannot publish under a prefix that disagrees with its own work.

## Three costs this keeps proportional

**Reading is forward-only.** Keys carry the publishing replica's own `db_version` range in fixed-width
hex, so they sort in the order changes were made and a remembered key resumes exactly where the last
sync stopped. A sync costs what changed, not what exists.

**Peers cost one listing.** Discovery uses a grouped listing (`ListPrefixesAsync`), so finding out who
exists costs one response naming the *devices* rather than one listing every object they have written —
which would undo the point of the watermark.

**Only your own work is published.** A replica's feed carries every change it has ever accepted from a
peer, still stamped with the originating `site_id`. Publishing it unfiltered would have every device
re-uploading every other device's history, so `ReadLocalChangesAsync()` is what goes up. Uploads are
batched (`MaxChangesPerObject`, default 5000), because object storage charges per request and one object
per change would be the expensive shape.

## Why a peer watermark is a key, not a version

A change's `db_version` is assigned by whichever database it is read from — applying a peer's change
stamps it with *this* replica's next version — so the same change has a different version in every
database that holds it. "Everything peer X has after N" is therefore unanswerable from versions, and the
key ordering in the bucket is the only portable watermark. Getting this wrong does not fail loudly; it
silently skips changes.

The watermark advances **after** the changes are committed locally, so an interrupted pull is retried
rather than skipped. Skipped changes never come back — the peer has no reason to publish them again.

## Offline is the normal case, not an error

The **database is the queue.** An edit is committed to SQLite by `SaveChanges` before any of this runs,
so there is nothing to lose if the bucket is unreachable and no "offline mode" to enter. A failed sync
leaves the database untouched and the next one publishes the same changes — safe precisely because
applying a change twice does nothing.

`CrdtSyncPhase.Offline` is deliberately not a failure state. Showing it as an error trains people to
ignore the indicator that matters.

There is **no conflict count** in the status, on purpose. Merging is per column and automatic, so
nothing was silently discarded and there is nothing a user could be asked to resolve. Reporting a
conflict would be reporting a decision that was never made.

## The sync state is a cache, not a record

`ICrdtSyncStore` holds the peer watermarks and what this replica last published:

```csharp
var engine = new CrdtSyncEngine(objectStore, feed, new InMemoryCrdtSyncStore());
```

Losing all of it costs re-uploading and re-reading — **never data** — because SQLite already holds the
truth. That is a materially better bargain than a queue-based sync such as
[`Rask.Sync.Client`](sync-client.md), where losing the queue loses a user's offline edits, and it is why
an in-memory store is a legitimate default here rather than only a test double.

A fresh state is answered *from the bucket* rather than assumed to mean "never published", so a
reinstalled device with the same database does not re-upload its whole history.

## Security and scope

Every device holds bucket credentials and a full copy of the database. There is no partial replication
and no per-row authorization, so this targets **one user across many devices, and small trusted
teams** — not multi-tenant apps. Anyone who can read the bucket can read everything in it.

Give each device its own credentials if the store supports it, so one can be revoked without rotating
the rest.

## A working sample

[`samples/Rask.Example.Crdt`](../samples/Rask.Example.Crdt) runs three devices — Phone, Laptop,
Tablet — each with its own database, sharing a bucket and nothing else. Each can be taken offline
independently:

```bash
RASK_CRSQLITE_PATH=/path/to/crsqlite.dylib dotnet run --project samples/Rask.Example.Crdt
```

The thing to try: take two devices offline, edit **different fields of the same todo** on each — the
priority on one, the done flag on the other — then bring both back and press *Sync everyone*. Both
edits survive. Do the same with a `LastModified` column and one of them is gone.

The bucket there is a [`FolderObjectStore`](object-storage.md#a-folder-as-a-bucket) so it runs with no
credentials; swapping in `S3ObjectStore` is the only change needed to put the same three devices on the
internet.

## See also

- [Multi-writer SQLite](sqlite-crdt.md) — the merge itself, and the EF Core integration.
- [Object storage](object-storage.md) — the S3 / Azure Blob client this runs on.
- [Syncing between devices](sync-client.md) — the same bucket doctrine for apps not backed by SQLite.
