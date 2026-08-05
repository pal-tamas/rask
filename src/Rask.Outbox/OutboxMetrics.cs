using System.Diagnostics.Metrics;

namespace Rask.Outbox;

/// <summary>
/// OpenTelemetry-compatible metrics for the outbox pillar, published on the <see cref="MeterName"/> meter.
/// Registered as a singleton by <c>AddRaskOutbox</c> and written to by <see cref="OutboxProcessor{TContext}"/>.
/// <para>
/// Read locally with <c>dotnet-counters monitor --counters Rask.Outbox</c>, or export with
/// <c>MeterProvider.AddMeter(OutboxMetrics.MeterName)</c>.
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
public sealed class OutboxMetrics : IDisposable
{
    /// <summary>The meter name to subscribe to.</summary>
    public const string MeterName = "Rask.Outbox";

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
    public OutboxMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);

        _processed = _meter.CreateCounter<long>(
            "rask.outbox.processed", "{message}", "Outbox messages published successfully.");
        _failed = _meter.CreateCounter<long>(
            "rask.outbox.failed", "{attempt}", "Outbox attempts that threw. Counts every attempt, not every message.");
        _deadLettered = _meter.CreateCounter<long>(
            "rask.outbox.deadlettered", "{message}", "Outbox that exhausted MaxAttempts and will not be retried.");
        _interrupted = _meter.CreateCounter<long>(
            "rask.outbox.interrupted", "{message}",
            "Messages cancelled by shutdown after ShutdownGracePeriod. They re-publish on restart and count no attempt.");
        _duration = _meter.CreateHistogram<double>(
            "rask.outbox.duration", "ms", "Wall-clock duration of publishing one outbox message.");

        _pendingGauge = _meter.CreateObservableGauge(
            "rask.outbox.pending", () => Volatile.Read(ref _pending),
            "{message}", "Outbox not yet processed and not yet exhausted.");
        _deadLetterGauge = _meter.CreateObservableGauge(
            "rask.outbox.deadletters", () => Volatile.Read(ref _deadLetters),
            "{message}", "Outbox that have given up. The number worth alerting on.");
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

    /// <summary>Records a message that completed, and how long it took.</summary>
    /// <remarks>
    /// Tagged by the registered type, which is a closed set fixed at build time by the source
    /// generator — so this cannot become the unbounded-cardinality trap that tagging by, say, job id would.
    /// </remarks>
    public void Processed(string messageType, double milliseconds)
    {
        var tag = new KeyValuePair<string, object?>("message.type", messageType);
        _processed.Add(1, tag);
        _duration.Record(milliseconds, tag);
    }

    /// <summary>Records a failed attempt. One message retried five times counts five times here.</summary>
    public void Failed(string messageType) =>
        _failed.Add(1, new KeyValuePair<string, object?>("message.type", messageType));

    /// <summary>Records a message crossing into dead-letter state — counted once, on the attempt that exhausts it.</summary>
    public void DeadLettered(string messageType) =>
        _deadLettered.Add(1, new KeyValuePair<string, object?>("message.type", messageType));

    /// <summary>
    ///     Records a message that shutdown cancelled after its grace period. Deliberately not a
    ///     <see cref="Failed" />: the publish did not fail, it was interrupted, and it runs again on
    ///     restart with its attempt count untouched. A nonzero rate means
    ///     <c>OutboxOptions.ShutdownGracePeriod</c> is shorter than the work — and, since an interrupted
    ///     message re-publishes whole, that any non-idempotent handler is repeating its side effects.
    /// </summary>
    public void Interrupted(string messageType) =>
        _interrupted.Add(1, new KeyValuePair<string, object?>("message.type", messageType));

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
