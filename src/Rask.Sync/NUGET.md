# Rask.Sync

The correctness core of offline-first sync: a hybrid logical clock, an append-only operation log, and a
merge that lands every replica on the same state no matter what order things arrive in.

**Pure logic — no I/O, no transport, no database.** It does not know where ops come from or go. That is
deliberate: this is the part that must be right, so it is the part with nothing else in it.

```bash
dotnet add package Rask.Sync
```

## The three properties

- **Order-independent** — apply the same ops in any order, get the same state.
- **Idempotent** — apply an op twice, nothing changes.
- **Convergent** — two replicas that saw the same ops hold identical state.

Together these are what remove the need for a server. A client never has to know what it already sent,
never has to coordinate with a peer, and never has to be right about the order.

## Use it

```csharp
var clock = new HybridLogicalClock(nodeId: deviceId);   // stable per install
var state = new SyncState();

// A local edit: only the fields that changed.
var op = SyncOp.SetFields("Todo", todoId, clock.Tick(),
    new Dictionary<string, string> { ["done"] = "true" });

state.Apply(op);

// Something arriving from a peer.
clock.Observe(incoming.Stamp);          // so later local edits sort after it
var conflicts = state.Apply(incoming);
```

Ops carry **changed fields**, not whole rows — so two devices editing different fields of the same record
while offline both keep their edit. Values are raw JSON text, opaque to the engine.

## Conflicts are reported, not hidden

Last-writer-wins **loses data by design**. Two people edit the same field offline, one edit survives, and
the other is gone. No rule avoids that — something has to lose. What can be avoided is nobody being told.

Every merge that discards another node's value returns a `SyncConflict` carrying both values and both
stamps, so an application can surface it, log it, or offer the losing value back. Merging stays fully
automatic; nothing waits for a human.

What is *not* reported: a device overwriting its own earlier value, two nodes writing the same value, and
duplicate delivery. A conflict feed that fires on every ordinary save is one people learn to ignore.

## Why a hybrid logical clock

Ordering offline edits by `DateTimeOffset.UtcNow` is the classic way to lose data: device clocks disagree,
users set them by hand, and they run backwards over NTP corrections. An edit made later can carry an
earlier timestamp, so last-writer-wins discards the newer value and nothing reports a problem.

An HLC keeps wall time as its physical component but never moves backwards, and advances on every message
it observes — so anything issued after receiving an op sorts after it, whatever the two devices believe
the time to be. Stamps are fixed-width hex, so **sorting them as strings equals comparing them as values**,
which is what lets a log be ordered by object key with no parsing and no index.

Node identity is part of the stamp and is the final tie-break. Without it two devices can mint identical
stamps and the winner depends on arrival order — which is divergence, not a merge.

## Requirements

Rows are addressed by entity name plus a `Guid`. An offline insert must mint its own key, and a
database-assigned identity cannot be issued without a round trip.

---

Part of [Rask](https://github.com/pal-tamas/rask). Usable on its own — it has no dependencies at all.
