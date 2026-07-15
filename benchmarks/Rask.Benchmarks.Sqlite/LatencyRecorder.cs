using System.Diagnostics;

namespace Rask.Benchmarks.Sqlite;

/// <summary>Percentiles and counts for one window (or a whole run).</summary>
internal sealed record LatencyStats(
    long Ops,
    double DurationSeconds,
    double P50Ms,
    double P90Ms,
    double P95Ms,
    double P99Ms,
    double P999Ms,
    double MaxMs,
    long BusyErrors,
    long SqliteErrors,
    long OtherErrors)
{
    internal long ErrorOps => BusyErrors + SqliteErrors + OtherErrors;

    internal double Throughput => DurationSeconds > 0 ? Ops / DurationSeconds : 0;

    internal double ErrorRatio => Ops + ErrorOps > 0 ? (double)ErrorOps / (Ops + ErrorOps) : 0;

    internal static LatencyStats Empty(double durationSeconds) =>
        new(0, durationSeconds, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>One VU's samples and counters for a closed window, detached from the live recorder.</summary>
internal sealed record VuSnapshot(long[] Ticks, long BusyErrors, long SqliteErrors, long OtherErrors);

/// <summary>
/// Records one virtual user's latencies. Each VU is the only writer, but the runner reads across VUs to close
/// a window while they are still running, so writes and snapshots take the recorder's lock. It is
/// per-recorder and only ever contended at a window boundary, so in practice every acquisition is
/// uncontended (~20ns) against operations costing tens of microseconds and up — the harness must not become
/// the bottleneck it is trying to measure.
/// </summary>
internal sealed class VuRecorder
{
    private readonly Lock _gate = new();
    private long[] _ticks = new long[4096];
    private int _count;
    private long _busy;
    private long _sqlite;
    private long _other;

    /// <summary>
    /// Every commit this VU ever made, warmup included, never reset by a snapshot. The lost-write check
    /// compares this against the row count, and both then span exactly the same interval — the VU's whole
    /// life — so the comparison has no window to race against.
    /// </summary>
    internal long TotalOk { get; private set; }

    /// <summary>The first non-busy failure this VU saw, kept verbatim so a surprise can be diagnosed.</summary>
    internal string? FirstError { get; private set; }

    internal void Record(long elapsedTicks, in OpOutcome outcome)
    {
        lock (_gate)
        {
            if (outcome.Kind is OutcomeKind.SqliteError or OutcomeKind.Other or OutcomeKind.Busy)
            {
                FirstError ??= outcome.ErrorType ?? $"rc={outcome.SqliteErrorCode}";
            }

            switch (outcome.Kind)
            {
                case OutcomeKind.Ok:
                    TotalOk++;
                    if (_count == _ticks.Length)
                    {
                        Array.Resize(ref _ticks, _ticks.Length * 2);
                    }

                    _ticks[_count++] = elapsedTicks;
                    break;
                case OutcomeKind.Busy:
                    _busy++;
                    break;
                case OutcomeKind.SqliteError:
                    _sqlite++;
                    break;
                case OutcomeKind.Other:
                    _other++;
                    break;
                case OutcomeKind.Cancelled:
                    // Truncated by the deadline: neither a success nor a failure of the code under test, so
                    // it is dropped rather than counted or timed.
                    break;
            }
        }
    }

    /// <summary>Atomically detaches everything recorded so far and starts the next window empty.</summary>
    internal VuSnapshot TakeSnapshot()
    {
        lock (_gate)
        {
            var snapshot = new VuSnapshot(_ticks.AsSpan(0, _count).ToArray(), _busy, _sqlite, _other);
            _count = 0;
            _busy = _sqlite = _other = 0;
            return snapshot;
        }
    }
}

internal static class Percentiles
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Merges every VU's snapshot and computes exact nearest-rank percentiles. Raw samples (rather than a
    /// histogram) keep this exact and dependency-free; the volumes are tractable because
    /// <see cref="LoadRunner"/> summarises and discards each window as it closes.
    /// </summary>
    internal static LatencyStats Summarise(IReadOnlyList<VuSnapshot> snapshots, double durationSeconds)
    {
        var total = 0;
        long busy = 0, sqlite = 0, other = 0;
        foreach (var snapshot in snapshots)
        {
            total += snapshot.Ticks.Length;
            busy += snapshot.BusyErrors;
            sqlite += snapshot.SqliteErrors;
            other += snapshot.OtherErrors;
        }

        if (total == 0)
        {
            return LatencyStats.Empty(durationSeconds) with
            {
                BusyErrors = busy,
                SqliteErrors = sqlite,
                OtherErrors = other,
            };
        }

        var merged = new long[total];
        var offset = 0;
        foreach (var snapshot in snapshots)
        {
            snapshot.Ticks.CopyTo(merged.AsSpan(offset));
            offset += snapshot.Ticks.Length;
        }

        Array.Sort(merged);

        return new LatencyStats(
            total,
            durationSeconds,
            Quantile(merged, 0.50),
            Quantile(merged, 0.90),
            Quantile(merged, 0.95),
            Quantile(merged, 0.99),
            Quantile(merged, 0.999),
            merged[^1] * TicksToMs,
            busy,
            sqlite,
            other);
    }

    /// <summary>Nearest-rank on an ascending array: the smallest value at or above the requested rank.</summary>
    private static double Quantile(long[] sorted, double quantile)
    {
        var rank = (int)Math.Ceiling(quantile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)] * TicksToMs;
    }
}
