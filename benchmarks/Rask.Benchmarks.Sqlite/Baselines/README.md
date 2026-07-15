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

## The unexplained `mixed-ef` BUSY (open)

One sweep reported **2 escaped `SQLITE_BUSY` on `mixed-ef` @ 128 VUs in ~542,000 operations (0.0004%)**. It
has never recurred and is **not root-caused**. Recorded here rather than dropped, because the honest status
is "one unexplained observation", not "fixed".

The one hard constraint: **that run was 15s and the arm's retry budget is 30s**, so the strategy cannot have
exhausted its budget. It either gave up early or never saw the exception. Note the same sweep also produced
the only occurrence of the `raw-nonblocking` error burst below — two unrelated anomalies in one run, neither
seen before or since, which is itself evidence about that run rather than about the library.

Ruled out, each with a committed regression test in `tests/Rask.SQLite.EntityFrameworkCore.Tests`:

| Hypothesis | Verdict |
|---|---|
| `PRAGMA journal_mode=WAL` (lock-taking) runs on every open via the interceptor, and `configureRetry` sets `busy_timeout=0`, removing the wait that the pragma ordering exists to provide | **No.** `RaskSqliteOpenUnderLockTests` — opening and reading while another connection holds the write lock does not throw. `journal_mode=WAL` only needs the exclusive lock when it *changes* the mode; on an already-WAL database it is a no-op. |
| The measurement deadline cancels an op mid-retry, and the harness misreads the result as an escaped BUSY (2 ≈ the ops in flight at 128 VUs) | **No.** `RaskSqliteCancelledRetryTests` — a cancelled retrying `SaveChanges` throws `OperationCanceledException` at the top level, which the classifier scores as `Cancelled`, not `Busy`. |

Not reproduced by: ~2.1M targeted `mixed-ef` @ 128 VU operations (8x the original exposure), a full `all`
sweep, and 3x (pinned-WAL soak -> `mixed-ef` @ 128) to recreate the original sequence.

If you see it again, the harness now prints the exception's **extended** result code and full chain
(`first error was rc=5/ext=...`). That extended code is the thread to pull: it separates plain `SQLITE_BUSY`
from `SQLITE_BUSY_SNAPSHOT` (an unretryable deferred read-then-write upgrade) and `SQLITE_BUSY_RECOVERY` (a
WAL/-shm recovery race at open) — which have different causes and different fixes.

- The harness has also once logged a burst of non-busy errors on `raw-nonblocking` mid-sweep, in that same
  sweep, that no later run reproduced. It predates the current error reporting, so its exception was never
  captured.
