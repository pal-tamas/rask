# SQLite load-harness baselines

`sqlite-load.csv` is the committed reference run for `Rask.SQLite` and
`Rask.SQLite.EntityFrameworkCore`. It is **documentation, not a gate** — the same posture this repo already
takes for `FullPayloadBytes` and the `mem-footprint` report. Timing numbers are not reproducible across
machines, so nothing compares against this file automatically.

Hardware: Apple M4 (Arm64), .NET 10, Concurrent Server GC, APFS SSD, database on `$TMPDIR`.

Reproduce:

```bash
dotnet run -c Release --project benchmarks/Rask.Benchmarks.Sqlite -- all --vus 1,32,128 --duration 15 \
  --out benchmarks/Rask.Benchmarks.Sqlite/Baselines/sqlite-load.csv
```

## What this harness is for, and why it is not BenchmarkDotNet

`Rask.Benchmarks/SqliteWriteContentionBenchmarks` (BenchmarkDotNet) measures the **per-operation cost** of a
fixed burst, and nightly already runs it. It cannot answer the question this harness exists for: sustained
throughput, tail latency, and error rates under N concurrent clients. BDN reports a mean; a load test needs
p99.9 and "did anything fail". Both are kept — they answer different questions.

## Reading the numbers

- **Closed loop.** Each virtual user keeps exactly one operation in flight, so latency is service time under
  N concurrent clients, not open-loop response time: a stall parks the VUs instead of queueing work behind
  them. Absolute p99 at a given VU count is therefore optimistic (this is coordinated omission). The *knee*
  of the throughput-vs-latency curve is robust to it, which is what the VU sweep is for.
- **Runs over 60s** report tail percentiles as the max of the per-window percentiles and p50 as the median of
  window p50s, because raw samples are discarded per window. Averaging percentiles is not arithmetic.
- **`lost`** must always be 0. It compares acknowledged commits against rows actually in the database over
  the VUs' whole life. A small *surplus* (rows the harness never counted) is expected and bounded by one per
  VU: the deadline can cancel an operation after its INSERT committed.

## What the gate (`check`) catches — and what it does not

**It does not detect a 20% throughput regression.** Nothing cheap does, on shared hardware. Every assertion
is an invariant or a ratio between two arms measured *in the same process, on the same box, in the same
run* — a 2-vCPU runner and an M4 disagree wildly about absolute milliseconds and agree completely that a
DELETE-mode reader is far slower than a WAL-mode one.

It catches: the retry loop breaking, writes being lost, WAL ceasing to do its job, the two write paths
ceasing to differ, and the EF `CommandTimeout(1)` lowering being dropped. That is the set of things that
actually break in this code.

`check --ci` runs Tier 1 only (hardware-independent); plain `check` adds the Tier 2 checks that need a real
box. Nightly runs `--ci` best-effort and uploads the CSV; the local gate is `scripts/run-sqlite-load-local.sh`.

## No unit tests here

`PayloadBytesReport` and `BundleSizeReport` have none either: the repo's precedent is that benchmark and
report tooling is verified by running it, and CLAUDE.md's "unit-test every feature" is about `src/`. The
percentile maths and the error classifier are kept as small static classes so they are reviewable by eye.
The gate itself is verified by deliberately breaking each invariant and checking it fails (see the PR).

## The `mixed-ef` BUSY: root-caused (and it was never a lock)

`mixed-ef` @ 128 VUs can rarely throw `SQLITE_BUSY` (first seen: 2 in ~542,000 ops). **The cause is now
known, and the earlier lock-contention framing on this page was wrong.** It is *not* a contended write lock,
and `busy_timeout` cannot fix it — which is exactly why the `busy_timeout`-restoring candidate below only
ever reduced it.

### Where it comes from

Reproduce it by forcing blocking gen2 collections inside the measured window; at 128 VUs it hits roughly
**1 run in 6**. The stack is unambiguous:

```
at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC(...)
at Microsoft.Data.Sqlite.SqliteConnection.Deactivate()
at Microsoft.Data.Sqlite.SqliteConnection.Close()
```

`Deactivate()` runs **no lock-taking SQL** (decompiled, Microsoft.Data.Sqlite 10.0.10). All it does is
*un-register the connection's user functions/collations* — via `sqlite3_create_function(name, null)` /
`sqlite3_create_collation(name, null)` — when a pooled connection is returned (`SqliteConnectionPool.Return`
→ `SqliteConnectionInternal.Deactivate` → `SqliteConnection.Deactivate`). EF Core registers ~13 of these on
every connection (`regexp`, `ef_add`, `ef_mod`, `ef_avg`, the `EF_DECIMAL` collation, …), so there is always
something to un-register.

And **`sqlite3_create_function(name, null)` returns `SQLITE_BUSY` — *"unable to delete/modify user-function
due to active statements"* — when a prepared statement is still active** on the connection (`nVdbeActive > 0`).
That is the BUSY. It has nothing to do with locking.

The active statement is an **orphaned reader**: a `SqliteCommand`/reader that was GC-collected but whose
`sqlite3_stmt` SafeHandle finalizer has not run yet. `SqliteConnection.Close()` only finalizes commands whose
`WeakReference` is still live (it iterates `_commands` and skips dead targets), so it misses the orphan; the
statement is still active when `Deactivate()` tries to modify the function table. Forcing gen2 GC widens the
window between "reader collected" and "statement finalized," which is why the repro needs it.

This corrects every fact the old lock story bent to fit:

- **A 15s run cannot exhaust a 30s retry budget** — right, but not because teardown "isn't a command." It's
  because this BUSY is instantaneous and non-retryable: `busy_timeout` governs the *lock* busy handler, not the
  function-table guard.
- **"It needs `configureRetry` (which sets `busy_timeout=0`)"** — a correlation, not the cause. `busy_timeout`
  never gated this call; enabling retry just raises throughput/churn, so orphaned-statement windows occur more
  often. A deterministic repro (below) throws with the default 5000 ms `busy_timeout` set.
- **It needs reads and writes** — reads supply the readers that get orphaned; write churn keeps the pool
  cycling connections through `Deactivate`.

### Deterministic reproduction

No GC luck required. On a pooled connection, register a function the way EF does, orphan an active statement,
then close:

```csharp
var c = new SqliteConnection("Data Source=app.db");   // pooling ON
c.Open();
c.CreateFunction("ef_demo", () => 1);                 // MDS-tracked, like EF's ef_* / regexp
raw.sqlite3_prepare_v2(c.Handle, "SELECT x FROM t;", out var stmt);
raw.sqlite3_step(stmt);                               // active statement, not a SqliteCommand
c.Close();                                            // -> Return -> Deactivate -> create_function(ef_demo, null)
// throws SqliteException rc=5: 'unable to delete/modify user-function due to active statements'
```

`Pooling=False` makes the same scenario pass (no pooled return → `Deactivate` never runs).

### What has been tried

| Attempt | Result |
|---|---|
| `PRAGMA journal_mode=WAL` (lock-taking) racing on every open | **Not it** — `RaskSqliteOpenUnderLockTests`. It only takes the exclusive lock when it *changes* mode; on an already-WAL database it is a no-op. |
| The measurement deadline cancelling an op mid-retry, misread as an escape | **Not it** — `RaskSqliteCancelledRetryTests`. A cancelled retrying `SaveChanges` throws `OperationCanceledException` at the top level, scored `Cancelled`. |
| **Candidate fix:** restore a native `busy_timeout` in a `ConnectionClosing` interceptor hook | **Reduced but never eliminated** — ~1 in 42 vs ~1 in 6. Now explained: `busy_timeout` was the wrong lever entirely (this BUSY isn't a lock wait); it only shifted timing/GC pressure. |

### Fix / mitigation

It is an **upstream Microsoft.Data.Sqlite pool-return behaviour** — `Deactivate()` un-registers functions
without tolerating the active-statements `SQLITE_BUSY` — present for any EF Core SQLite app that registers
functions, Rask or not. No safe Rask-side code fix exists: finalizing lingering statements from a
`ConnectionClosing` hook would break a still-live reader (you cannot tell an orphan from a live statement via
`sqlite3_next_stmt`), and silently disabling pooling would be a per-open pragma-cost regression. The honest,
correct guidance is `Pooling=False` on the EF connection string when this matters (verified to remove it),
and to raise it upstream. Documented for users in [`docs/sqlite.md`](../../../docs/sqlite.md).

### Unrelated: the one-off `raw-nonblocking` burst

- The harness also once logged a burst of non-busy errors on `raw-nonblocking` mid-sweep that no later run
  reproduced. It predates the current error reporting, so its exception was never captured. The raw
  immediate-transaction path is now hardened for a recurrence: it clears a leaked transaction before
  `BEGIN IMMEDIATE`, never returns a mid-transaction handle to the pool, and throws a `SqliteException`
  carrying the extended result code and autocommit state — so a repeat would be attributable rather than
  an opaque `SQLite Error 1: 'not an error'`.
