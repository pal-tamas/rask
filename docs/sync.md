# Offline-first merge (`Rask.Sync`)

> **In practice:** the part of offline sync that has to be right. Pure logic, no I/O — moving the log
> around is [object storage](object-storage.md)'s job.

Two devices edit the same data while offline. Both come back. What should the data be?

`Rask.Sync` answers that deterministically: a hybrid logical clock to order events across devices that
don't share a clock, an append-only log of operations, and a merge that lands every replica on the same
state regardless of what order things arrive in.

```bash
dotnet add package Rask.Sync
```

It has **no dependencies and does no I/O**. It doesn't know where ops come from or where they go. That is
the point — this is the piece where a mistake silently destroys a user's work, so it is the piece with
nothing else in it to hide behind.

## The three properties

| Property | Means | Why it matters |
|---|---|---|
| Order-independent | Same ops in any order → same state | Object storage promises nothing about listing order |
| Idempotent | Applying an op twice changes nothing | A retried upload or a re-read log costs nothing |
| Convergent | Same ops seen → identical state | Two devices agree without ever talking to each other |

Together they are what removes the need for a server. A client never has to know what it already sent,
never has to coordinate with a peer, and never has to be right about the order.

## Using it

```csharp
var clock = new HybridLogicalClock(nodeId: deviceId);   // stable per install, distinct per device
var state = new SyncState();

// A local edit — only the fields that actually changed.
var op = SyncOp.SetFields("Todo", todoId, clock.Tick(),
    new Dictionary<string, string> { ["done"] = "true" });
state.Apply(op);

// Something that arrived from a peer.
clock.Observe(incoming.Stamp);              // later local edits now sort after it
var conflicts = state.Apply(incoming);
```

## Ops carry changed fields, not rows

```json
{"e":"Todo","id":"7f3a2b91-…","t":"0000019FE001-0000-node-a","set":{"done":true}}
```

Per-field is what lets two devices edit *different* fields of the same record offline and both keep their
work. A whole-row op would silently discard one of them. Values are raw JSON, opaque to the engine — it
compares and replaces them whole and never needs to know your types.

Deletes are tombstones (`"d":true`). Without one, a row would come back the moment any older op was
replayed — which is exactly what a peer re-reading the log does. An edit genuinely *newer* than the delete
does revive the row, which is both what a user expects and what makes delete and edit commute.

## Conflicts are reported, never hidden

Last-writer-wins **loses data by design**. Two people edit the same field offline; one edit survives and
the other is gone. No cleverer rule avoids this — something has to lose. What *can* be avoided is nobody
finding out.

```csharp
foreach (var conflict in state.Apply(incoming))
{
    // conflict.Kind, .Field, .WinningValue, .LosingValue, .WinningNode, .LosingNode
}
```

| Kind | What happened |
|---|---|
| `Overwritten` | An arriving op replaced another device's value |
| `Discarded` | An arriving op lost to a newer value already held |
| `DeleteHidEdits` | A delete won over another device's edits |
| `EditRevivedDeleted` | An edit landed after another device's delete, so the row is back |

Merging stays fully automatic — nothing blocks on a human. But the application can surface what was lost,
keep an audit trail, or offer the losing value back.

**Not** reported: a device overwriting its own earlier value, two devices writing the same value, and
duplicate delivery. A conflict feed that fires on every ordinary save is one people learn to ignore, which
is the same outcome as not reporting at all.

## Why not `DateTimeOffset.UtcNow`

Ordering offline edits by wall clock is the classic way to lose data. Device clocks disagree by minutes,
users set them by hand, and they run backwards over NTP corrections and daylight-saving changes. An edit
made *later* can carry an *earlier* timestamp, so last-writer-wins discards the newer value — silently.

A hybrid logical clock keeps wall time as its physical component, so stamps still mean something to a
human, but it never moves backwards and it advances on every message it observes. Once a device has seen a
remote stamp, everything it issues afterwards sorts after it — so a reply always beats what it replied to,
whatever either clock believes.

Two details that carry weight:

- **Stamps are fixed-width hex**, so sorting them as *strings* equals comparing them as *values*. That is
  what lets a log be ordered by object key alone, with no parsing and no index.
- **Node identity is part of the stamp** and is the final tie-break. Without it two devices can mint
  identical stamps and the winner depends on which arrived first — a different answer per replica, which
  is divergence, not a merge.

An HLC gives a **total order, not causality**. Genuinely simultaneous edits are ordered arbitrarily but
*consistently*, which is what makes the merge deterministic. Telling a user what was overwritten is the
conflict record's job, not the clock's.

## Requirements and limits

- **Rows are addressed by entity name plus a `Guid`.** An offline insert must mint its own key; a
  database-assigned identity cannot be issued without a round trip, and two devices inserting offline
  would collide.
- **This is not a CRDT.** Per-field last-writer-wins over a total order, not concurrent-edit merging
  within a field. Two people typing into the same text field will not have their edits interleaved — one
  wins, and the other is reported.
- **`SyncState` is not thread-safe.** Apply from one place, as a replica would. `HybridLogicalClock` *is*
  thread-safe, because a debounced flush and a user edit really can stamp at the same moment.

## See also

- [Object storage](object-storage.md) — moving the log to and from a bucket.
- [SQLite](sqlite.md) — the local database a merged state is materialised into.
