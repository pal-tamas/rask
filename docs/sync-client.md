# Syncing between devices (`Rask.Sync.Client`)

> **In practice:** the piece that makes several devices share data with **no server between them**.
> Joins [the merge engine](sync.md) to [a bucket](object-storage.md).

```bash
dotnet add package Rask.Sync.Client
```

```csharp
var clock  = new HybridLogicalClock(nodeId: deviceId);   // stable per install, distinct per device
var state  = new SyncState();
var engine = new SyncEngine(objectStore, syncStore, clock, state);

// A local edit. Never touches the network.
await engine.RecordAsync(SyncOp.SetFields("Todo", id, clock.Tick(),
    new Dictionary<string, string> { ["done"] = "true" }));

await engine.SyncAsync();                     // push what's queued, then pull peers
engine.Changed += status => Render(status);   // synced / pending / offline / conflicts
```

## The layout, and why it needs no coordination

```
clients/
  <device-a>/ops/{minStamp}__{maxStamp}.json
  <device-b>/ops/{minStamp}__{maxStamp}.json
```

**Each device writes only under its own prefix and never touches another's.** That single rule is what
removes the need for a server: no two clients ever write the same key, so there is nothing to lock,
nothing to retry on conflict, and no lease to renew or to leak if a device disappears mid-write.

Everything else follows from it:

- **Forward-only reads.** Keys carry the hybrid logical clock in fixed-width hex, so they sort in the
  order things happened. Remembering the last key read from a peer resumes exactly there — the cost of a
  sync is what changed, not what exists. This is the reason stamps are hex rather than a friendlier format.
- **Cheap peer discovery.** A grouped listing returns the *devices*, not their objects. Without it,
  finding out who else exists would mean listing everything anyone has ever written, which would undo the
  watermark entirely.
- **Batched uploads.** One object per sync, keyed by the clock range it covers. Object storage charges per
  request, so one object per edit would be the expensive shape.

## Offline is the normal case

`RecordAsync` applies the edit locally and queues it. It never touches the network, so the app behaves
the same with or without connectivity — there is no "offline mode" to enter.

A failed upload leaves the queue intact and the next sync re-sends it. Re-sending is harmless because
applying an operation twice changes nothing, which is what lets the engine retry without tracking what
the bucket already has.

`SyncPhase.Offline` is **not** an error state. Being offline is the operating mode this exists for, and
showing it as a failure teaches people to ignore the indicator that matters.

## Status is the user-facing half

```csharp
engine.Changed += status =>
{
    if (status.HasUnsyncedWork) ShowPendingBadge(status.Pending);
    if (status.Conflicts > 0)   ShowConflicts(engine.Conflicts);
};
```

| Field | Answers |
|---|---|
| `Pending` | *If I close this tab now, do I lose anything?* |
| `Conflicts` | *Did syncing throw away something I typed?* |
| `Phase` | Idle · Syncing · Offline · Faulted |
| `Peers` | How many other devices were seen at the last sync |

Those first two are the questions offline-first apps usually answer with a spinner and with silence.
Neither can be answered unless the engine keeps count, which is why they are on the status rather than
left to the app.

## Local state

`ISyncStore` holds the pending queue and the per-peer watermarks across reloads. It is deliberately tiny
and not tied to a browser, so the engine stays unit-testable.

One asymmetry decides how much care an implementation needs: **losing the queue loses a user's offline
edits outright**, while losing the watermarks costs only re-reading objects — replay is idempotent, so
nothing breaks. The queue is the part that must be durable.

`InMemorySyncStore` ships for tests and for a server-side replica. A browser app should back it with
[OPFS](apis/origin-private-file-system.md); that implementation belongs in the app, because `Rask.Core`
is not published as a package and a package that depended on it could not be restored.

## Limits

- **Every device holds bucket credentials and can read everything.** This targets one user across many
  devices, and small trusted teams — not multi-tenant apps. See
  [object storage](object-storage.md#credentials) on scoping the credential itself.
- **Per-field last-writer-wins**, not concurrent-edit merging within a field. Two people typing into the
  same text field will not have their edits interleaved — one wins, and the other is reported.
- **Log growth is not yet bounded.** Compaction is a separate piece; until then the log keeps every
  operation ever written.

## See also

- [Offline-first merge](sync.md) — the clock, the log, and the merge rules.
- [Object storage](object-storage.md) — the bucket, credentials, and the CORS trap.
