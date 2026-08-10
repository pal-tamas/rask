# Rask.Sync.Client

Shares data between devices through an object-storage bucket, with **no server in between**. Joins
[`Rask.Sync`](https://www.nuget.org/packages/Rask.Sync)'s merge engine to
[`Rask.ObjectStore`](https://www.nuget.org/packages/Rask.ObjectStore).

```bash
dotnet add package Rask.Sync.Client
```

## Use it

```csharp
var clock  = new HybridLogicalClock(nodeId: deviceId);   // stable per install, distinct per device
var state  = new SyncState();
var engine = new SyncEngine(objectStore, syncStore, clock, state);

// A local edit. Never touches the network — applies now, queues for later.
await engine.RecordAsync(SyncOp.SetFields("Todo", id, clock.Tick(),
    new Dictionary<string, string> { ["done"] = "true" }));

// Upload what's queued, then read what peers wrote.
await engine.SyncAsync();

engine.Changed += status => Render(status);   // synced / pending / offline / conflicts
```

## How it avoids needing a server

**Each device writes only under its own prefix** — `clients/{id}/ops/` — and never touches another's.
No two clients ever write the same key, so there is nothing to lock, nothing to retry on conflict, and
no lease to renew or leak. Everything else follows from that one rule.

**Reading is forward-only.** Keys carry the hybrid logical clock in fixed-width hex, so they sort in the
order things happened and a remembered key resumes exactly where the last sync stopped. Peers are found
with a grouped listing, so discovery costs one response listing the *peers* rather than one listing every
object they have ever written.

**Uploads are batched per sync**, keyed by the clock range they cover. Object storage charges per
request, so one object per operation would be the expensive shape.

## Offline is the normal case, not an error

`RecordAsync` never touches the network. The app behaves identically with or without connectivity, and
the queue drains on the next sync. A failed upload leaves the queue intact, and re-sending is harmless
because applying an operation twice changes nothing.

`SyncStatus.Pending` answers the question that actually matters — *if I close this tab now, do I lose
anything?* — which most offline-first apps answer with a spinner. `SyncStatus.Conflicts` answers the
other one: *did syncing throw away something I typed?*

`SyncPhase.Offline` is deliberately not a failure state. Being offline is the operating mode this exists
for, and showing it as an error trains people to ignore it.

## Local state

`ISyncStore` holds the pending queue and the per-peer watermarks. One asymmetry is worth knowing when
choosing an implementation: **losing the queue loses a user's offline edits**, while losing the
watermarks costs only re-reading objects, because replay is idempotent.

`InMemorySyncStore` ships for tests and server-side replicas. A browser app should back it with OPFS —
that implementation lives in the app rather than here, because `Rask.Core` is not published as a package.

## Scope

Every device holds bucket credentials and can read everything, so this targets **one user across many
devices, and small trusted teams** — not multi-tenant apps. Conflict resolution is per-field
last-writer-wins, not concurrent-edit merging within a field.

---

Part of [Rask](https://github.com/pal-tamas/rask).
