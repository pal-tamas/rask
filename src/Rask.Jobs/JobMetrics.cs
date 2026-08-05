using System.Diagnostics.Metrics;

namespace Rask.Jobs;

/// <summary>
/// OpenTelemetry-compatible metrics for the jobs pillar, published on the <see cref="MeterName"/> meter.
/// Registered as a singleton by <c>AddRaskJobs</c> and written to by <see cref="JobProcessor{TContext}"/>.
/// <para>
/// Read locally with <c>dotnet-counters monitor --counters Rask.Jobs</c>, or export with
/// <c>MeterProvider.AddMeter(JobMetrics.MeterName)</c>.
/// </para>
/// </summary>
/// <remarks>
/// <b>The queue-depth gauges are sampled, not computed on observation.</b> An observable-gauge callback runs
/// on the collector's schedule — every second under <c>dotnet-counters</c> — and answering it with
/// <c>COUNT(*)</c> would put an unbounded read load on the same SQLite file the processors are writing to,
/// purely because somebody attached a listener. Instead the processor offers a sample once per poll, and
/// only while <see cref="WantsQueueDepth"/> says a listener is actually subscribed. Nobody listening costs
/// nothing at all.
/// </remarks>
public sealed class JobMetrics : IDisposable
{
    /// <summary>The meter name to subscribe to.</summary>
    public const string MeterName = "Rask.Jobs";

    private readonly Meter _meter;
    private readonly Counter<long> _processed;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _deadLettered;
    private readonly Counter<long> _interrupted;
    private readonly Histogram<double> _duration;
    private readonly ObservableGauge<int> _pendingGauge;
    private readonly ObservableGauge<int> _deadLetterGauge;

    private int _pending;
    private int _deadLetters;

    /// <summary>
    /// Constructs the meter. Prefers the DI-supplied <see cref="IMeterFactory"/> so the instruments join the
    /// host's metrics pipeline and stay isolated between tests; falls back to a standalone
    /// <see cref="Meter"/> when no factory is registered.
    /// </summary>
    public JobMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);

        _processed = _meter.CreateCounter<long>(
            "rask.jobs.processed", "{job}", "Jobs that ran to completion.");
        _failed = _meter.CreateCounter<long>(
            "rask.jobs.failed", "{attempt}", "Job attempts that threw. Counts every attempt, not every job.");
        _deadLettered = _meter.CreateCounter<long>(
            "rask.jobs.deadlettered", "{job}", "Jobs that exhausted MaxAttempts and will not be retried.");
        _interrupted = _meter.CreateCounter<long>(
            "rask.jobs.interrupted", "{job}",
            "Jobs cancelled by shutdown after ShutdownGracePeriod. They re-run on restart and count no attempt.");
        _duration = _meter.CreateHistogram<double>(
            "rask.jobs.duration", "ms", "Wall-clock duration of a job execution.");

        _pendingGauge = _meter.CreateObservableGauge(
            "rask.jobs.pending", () => Volatile.Read(ref _pending),
            "{job}", "Jobs not yet processed and not yet exhausted.");
        _deadLetterGauge = _meter.CreateObservableGauge(
            "rask.jobs.deadletters", () => Volatile.Read(ref _deadLetters),
            "{job}", "Jobs that have given up. The number worth alerting on.");
    }

    // Exposed for tests so a MeterListener can scope to this exact meter INSTANCE. Filtering by meter
    // name isn't enough: xUnit runs test classes in parallel, each with its own provider and its own
    // equally-named meter, so a name filter collects other tests' measurements too.
    internal Meter Meter => _meter;

    /// <summary>
    /// <c>true</c> when something is actually collecting the queue-depth gauges, so the processor should pay
    /// for the two counts. <c>false</c> — the normal case — means it shouldn't.
    /// </summary>
    public bool WantsQueueDepth => _pendingGauge.Enabled || _deadLetterGauge.Enabled;

    /// <summary>Publishes a queue-depth sample taken by the processor's poll.</summary>
    public void ObserveQueueDepth(int pending, int deadLetters)
    {
        Volatile.Write(ref _pending, pending);
        Volatile.Write(ref _deadLetters, deadLetters);
    }

    /// <summary>Records a job that completed, and how long it took.</summary>
    /// <remarks>
    /// Tagged by the registered job type, which is a closed set fixed at build time by the source
    /// generator — so this cannot become the unbounded-cardinality trap that tagging by, say, job id would.
    /// </remarks>
    public void Processed(string jobType, double milliseconds)
    {
        var tag = new KeyValuePair<string, object?>("job.type", jobType);
        _processed.Add(1, tag);
        _duration.Record(milliseconds, tag);
    }

    /// <summary>Records a failed attempt. A job retried five times counts five times here.</summary>
    public void Failed(string jobType) =>
        _failed.Add(1, new KeyValuePair<string, object?>("job.type", jobType));

    /// <summary>Records a job crossing into dead-letter state — counted once, on the attempt that exhausts it.</summary>
    public void DeadLettered(string jobType) =>
        _deadLettered.Add(1, new KeyValuePair<string, object?>("job.type", jobType));

    /// <summary>
    ///     Records a job that shutdown cancelled after its grace period. Deliberately not a
    ///     <see cref="Failed" />: the job did not fail, it was interrupted, and it re-runs on restart with
    ///     its attempt count untouched. A nonzero rate means <c>JobOptions.ShutdownGracePeriod</c> is
    ///     shorter than the work — and, since an interrupted job re-runs from the top, that any
    ///     non-idempotent handler is repeating its side effects.
    /// </summary>
    public void Interrupted(string jobType) =>
        _interrupted.Add(1, new KeyValuePair<string, object?>("job.type", jobType));

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
