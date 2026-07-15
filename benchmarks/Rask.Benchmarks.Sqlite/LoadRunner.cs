using System.Diagnostics;

namespace Rask.Benchmarks.Sqlite;

/// <summary>One arm's measured result: the whole-run summary plus each closed window.</summary>
internal sealed record LoadResult(
    string Workload,
    string Arm,
    int Vus,
    LatencyStats Overall,
    IReadOnlyList<LatencyStats> Windows,
    ScenarioInvariants? Invariants,
    long WalBytesMax,
    long DbBytes)
{
    /// <summary>
    /// Whole-run percentiles are only exact for short runs; past that <see cref="LoadRunner"/> discards raw
    /// samples per window and <see cref="LatencyStats.P99Ms"/> is the <b>max of the window p99s</b>. That is
    /// conservative and defensible — averaging percentiles is not arithmetic.
    /// </summary>
    internal bool PercentilesAreWindowMaxima { get; init; }

    /// <summary>Every commit the harness counted across the arm's whole life, warmup included.</summary>
    internal long TotalOk { get; init; }

    /// <summary>The first non-busy failure any VU saw, for diagnosing a surprise in the error columns.</summary>
    internal string? FirstError { get; init; }

    /// <summary>
    /// Commits the harness counted that are <b>not</b> in the database. Both sides span the VUs' whole life
    /// (warmup included), so they cover the same interval and cannot race. Must be zero: anything else means
    /// SQLite did not keep a commit it acknowledged, which invalidates the row. Null for read-only arms.
    /// </summary>
    internal long? LostWrites => Invariants is null ? null : Math.Max(0, TotalOk - Invariants.RowsWritten);

    /// <summary>
    /// Rows in the database that the harness did not count as commits. A small surplus is expected and is
    /// not a defect: the deadline can cancel an operation after its INSERT committed but before the call
    /// returned, so the row lands while the op is recorded as cancelled. It is bounded by one per VU (each
    /// has at most one operation in flight), and anything beyond that means the harness is miscounting.
    /// </summary>
    internal long? UncountedRows => Invariants is null ? null : Math.Max(0, Invariants.RowsWritten - TotalOk);

    internal bool SurplusExceedsInFlight => UncountedRows > Vus;
}

internal static class LoadRunner
{
    /// <summary>Past this, per-window summaries replace exact whole-run percentiles (and expose drift).</summary>
    private static readonly TimeSpan ExactPercentileLimit = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Drives <paramref name="scenario"/> with <paramref name="vus"/> virtual users for a fixed duration and
    /// returns its throughput/latency/error profile.
    /// </summary>
    internal static async Task<LoadResult> RunAsync(
        LoadScenario scenario,
        string workload,
        int vus,
        LoadOptions options,
        CancellationToken cancellationToken)
    {
        // Give the pool enough threads up front. .NET's hill-climbing injects roughly one thread per 500ms
        // past the core count, so without this a 256-VU arm would spend its first minute measuring the
        // injection heuristic instead of SQLite. (Deliberately the opposite of
        // SqliteConcurrencyStressTests.Writers_far_exceeding_the_thread_pool_do_not_deadlock, which *shrinks*
        // the pool on purpose: that test proves the wait frees its thread, this measures throughput. The
        // harness must not be the bottleneck it is trying to measure.)
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        ThreadPool.SetMinThreads(Math.Max(vus + 8, minWorker), minIo);

        // The VUs are stopped by `deadline`, never by the caller's token directly: on Ctrl-C the measured
        // phase has to unwind through the same stop-then-drain path as a normal finish, or teardown would
        // delete the database out from under VUs that are still mid-operation.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await scenario.SetupAsync(cancellationToken).ConfigureAwait(false);

            var recorders = new VuRecorder[vus];
            for (var i = 0; i < vus; i++)
            {
                recorders[i] = new VuRecorder();
            }

            var windows = new List<LatencyStats>();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new CountdownEvent(vus);

            // Task.Run forces every VU onto the pool at once — a lazy Select would start them one by one,
            // each finishing before the next began, and measure no concurrency at all.
            var workers = new Task[vus];
            for (var i = 0; i < vus; i++)
            {
                var vuser = i;
                var recorder = recorders[i];
                workers[i] = Task.Run(async () =>
                {
                    ready.Signal();
                    await gate.Task.ConfigureAwait(false);
                    await DriveAsync(scenario, vuser, recorder, deadline.Token).ConfigureAwait(false);
                }, CancellationToken.None);
            }

            // Every VU is parked on the gate before the clock starts, so ramp-up never dilutes throughput.
            ready.Wait(CancellationToken.None);

            var walMonitor = new WalMonitor(scenario);
            var (overall, exact) = await MeasureAsync(
                gate, deadline, recorders, workers, windows, walMonitor, options)
                .ConfigureAwait(false);

            // Read the database only once every VU has stopped, so the row count and the harness's own commit
            // count describe exactly the same finished interval.
            var invariants = await scenario.VerifyAsync().ConfigureAwait(false);
            return new LoadResult(
                workload,
                scenario.Name,
                vus,
                overall,
                windows,
                invariants,
                walMonitor.MaxWalBytes,
                walMonitor.DbBytes())
            {
                PercentilesAreWindowMaxima = !exact,
                TotalOk = recorders.Sum(r => r.TotalOk),
                FirstError = recorders.Select(r => r.FirstError).FirstOrDefault(e => e is not null),
            };
        }
        finally
        {
            ThreadPool.SetMinThreads(minWorker, minIo);
            await scenario.TeardownAsync().ConfigureAwait(false);
        }
    }

    private static async Task<(LatencyStats Overall, bool Exact)> MeasureAsync(
        TaskCompletionSource gate,
        CancellationTokenSource deadline,
        VuRecorder[] recorders,
        Task[] workers,
        List<LatencyStats> windows,
        WalMonitor walMonitor,
        LoadOptions options)
    {
        gate.SetResult();

        // Warm up with the real workload, then throw the samples away: this JITs the whole path, builds the
        // EF model, fills the connection pool and warms SQLite's page cache, none of which is what we mean to
        // measure.
        await DelayAsync(options.Warmup, deadline.Token).ConfigureAwait(false);

        // Settle the heap BEFORE the discard snapshot, not after. The VUs never stop, so a blocking gen2
        // collect here would otherwise land inside the measured window: its ops would be counted against a
        // duration that excludes the pause (inflating ops/s), and the operations it stalled would show up as
        // a tail spike attributable to the harness rather than to SQLite — which the gate's MaxMs invariant
        // reads.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Snapshot(recorders);

        var exact = options.Duration <= ExactPercentileLimit;
        var started = Stopwatch.GetTimestamp();

        if (exact)
        {
            // Short run: keep every sample and compute exact whole-run percentiles.
            await DelayAsync(options.Duration, deadline.Token).ConfigureAwait(false);
            walMonitor.Sample();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;

            // Stop the VUs and let every in-flight operation settle before snapshotting, so the final window
            // can't race a VU that is still recording.
            await deadline.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(workers).ConfigureAwait(false);
            return (Percentiles.Summarise(Snapshot(recorders), elapsed), true);
        }

        // Long run: close a window at a time, summarise it, and drop its raw samples. A single whole-run
        // percentile would hide drift, which is the whole point of a soak.
        var remaining = options.Duration;
        long ops = 0, busy = 0, sqlite = 0, other = 0;
        double p99Max = 0, maxMs = 0;

        while (remaining > TimeSpan.Zero)
        {
            if (deadline.IsCancellationRequested)
            {
                break;
            }

            var slice = remaining < options.Window ? remaining : options.Window;
            var windowStart = Stopwatch.GetTimestamp();
            await DelayAsync(slice, deadline.Token).ConfigureAwait(false);
            walMonitor.Sample();

            var window = Percentiles.Summarise(
                Snapshot(recorders), Stopwatch.GetElapsedTime(windowStart).TotalSeconds);
            windows.Add(window);

            ops += window.Ops;
            busy += window.BusyErrors;
            sqlite += window.SqliteErrors;
            other += window.OtherErrors;
            p99Max = Math.Max(p99Max, window.P99Ms);
            maxMs = Math.Max(maxMs, window.MaxMs);
            remaining -= slice;
        }

        var totalSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        await deadline.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(workers).ConfigureAwait(false);

        // p50 across windows is reported as the median of the window p50s; the tail as the max of window
        // tails. Both are stated in the report so nobody reads them as exact whole-run percentiles.
        var p50s = windows.Select(w => w.P50Ms).Order().ToArray();
        var overall = new LatencyStats(
            ops,
            totalSeconds,
            p50s.Length > 0 ? p50s[p50s.Length / 2] : 0,
            windows.Count > 0 ? windows.Max(w => w.P90Ms) : 0,
            windows.Count > 0 ? windows.Max(w => w.P95Ms) : 0,
            p99Max,
            windows.Count > 0 ? windows.Max(w => w.P999Ms) : 0,
            maxMs,
            busy,
            sqlite,
            other);

        return (overall, false);
    }

    /// <summary>
    /// Waits, treating cancellation as "stop now" rather than as an error: on Ctrl-C the caller still wants
    /// the arms it already measured, printed and written to its CSV.
    /// </summary>
    private static async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Detaches every VU's samples, closing the current window.</summary>
    private static VuSnapshot[] Snapshot(VuRecorder[] recorders)
    {
        var snapshots = new VuSnapshot[recorders.Length];
        for (var i = 0; i < recorders.Length; i++)
        {
            snapshots[i] = recorders[i].TakeSnapshot();
        }

        return snapshots;
    }

    private static async Task DriveAsync(
        LoadScenario scenario,
        int vuser,
        VuRecorder recorder,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            OpOutcome outcome;
            try
            {
                outcome = await scenario.ExecuteAsync(vuser, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outcome = ErrorClassifier.Classify(ex, cancellationToken);
            }

            recorder.Record(Stopwatch.GetTimestamp() - started, outcome);
        }
    }
}
