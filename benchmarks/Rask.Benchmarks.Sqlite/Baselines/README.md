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

## The `mixed-ef` BUSY: located, not yet fixed

`mixed-ef` @ 128 VUs occasionally escapes `SQLITE_BUSY` (first seen: 2 in ~542,000 ops). **The cause is
known. The fix is not.** Do not treat the retry-enabled EF path as proven airtight under mixed load.

### Where it comes from

Reproduce it by forcing blocking gen2 collections inside the measured window (pauses lengthen lock holds and
widen the window); at 128 VUs it hits roughly **1 run in 6**. The stack is unambiguous:

```
at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC(...)
at Microsoft.Data.Sqlite.SqliteConnection.Deactivate()
at Microsoft.Data.Sqlite.SqliteConnection.Close()
```

The exception is **not** thrown by the query or by `SaveChanges`. It comes out of
`SqliteConnection.Close()` — Microsoft.Data.Sqlite's cleanup as a pooled connection is released, which runs
when the `DbContext` is disposed, *after* the caller's work has already committed.

That accounts for every otherwise-contradictory fact:

- **A 15s run cannot exhaust a 30s retry budget.** It doesn't need to: connection teardown is not a command,
  so no `IExecutionStrategy` covers it and *nothing retries it*.
- **It needs `configureRetry`.** That is what sets `busy_timeout=0`. With the 5000 ms default the cleanup
  simply waits the lock out; at 0 it throws instantly.
- **It needs reads and writes.** A context per operation churns pooled connections fast while writers hold
  the lock.

**This is a real defect, not a harness artifact:** `UseRaskSqlite(configureRetry: …)` can throw
`database is locked` out of *disposing* a `DbContext` under contention — after the work succeeded.

### What has been tried

| Attempt | Result |
|---|---|
| `PRAGMA journal_mode=WAL` (lock-taking) racing on every open | **Not it** — `RaskSqliteOpenUnderLockTests`. It only takes the exclusive lock when it *changes* mode; on an already-WAL database it is a no-op. |
| The measurement deadline cancelling an op mid-retry, misread as an escape | **Not it** — `RaskSqliteCancelledRetryTests`. A cancelled retrying `SaveChanges` throws `OperationCanceledException` at the top level, scored `Cancelled`. |
| **Candidate fix:** restore a native `busy_timeout` in a `ConnectionClosing` interceptor hook, so teardown can wait the lock out while commands keep `busy_timeout=0` | **Reduces but does not eliminate** — ~1 affected run in 42 vs ~1 in 6. Not shipped: a partial fix that still throws is worse than an accurate note, because it stops the next person looking. It presumably misses a close path (pool eviction, or a dispose that never raises `ConnectionClosing`). |

No deterministic reproduction yet. Both attempts failed — a single context writing and then disposing under a
held lock does **not** throw (200 iterations), and neither does raw Microsoft.Data.Sqlite connection churn
with `busy_timeout=0` against a held write lock. Whatever makes the driver's cleanup need the lock is
conditional and still unidentified; that condition is the next thing to find.

### If you pick this up

The harness prints the exception's **extended** result code and full chain (`first error was rc=5/ext=…`).
The open question is narrow: **what does `SqliteConnection.Deactivate()` execute that needs the write lock,
and under what condition?** Answer that and the fix follows.

- The harness also once logged a burst of non-busy errors on `raw-nonblocking` mid-sweep that no later run
  reproduced. It predates the current error reporting, so its exception was never captured. The raw
  immediate-transaction path is now hardened for a recurrence: it clears a leaked transaction before
  `BEGIN IMMEDIATE`, never returns a mid-transaction handle to the pool, and throws a `SqliteException`
  carrying the extended result code and autocommit state — so a repeat would be attributable rather than
  an opaque `SQLite Error 1: 'not an error'`.
