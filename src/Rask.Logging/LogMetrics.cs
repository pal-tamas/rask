using System.Diagnostics.Metrics;

namespace Rask.Logging;

/// <summary>
/// OpenTelemetry-compatible metrics for the log store, published on the <see cref="MeterName"/> meter.
/// Registered as a singleton by <c>AddRaskLogging</c>.
/// <para>
/// Read locally with <c>dotnet-counters monitor --counters Rask.Logging</c>, or export with
/// <c>MeterProvider.AddMeter(LogMetrics.MeterName)</c>.
/// </para>
/// </summary>
/// <remarks>
/// <b><c>rask.logs.dropped</c> is the number worth alerting on.</b> The buffer is bounded so that logging can
/// never become backpressure on a request, which means a store that cannot keep up loses entries instead of
/// slowing the app down. A non-zero drop rate says the log you are reading is incomplete — nothing else in the
/// system will tell you that.
/// <para>
/// <c>rask.logs.stored</c> is sampled by the writer's own sweep, not computed on observation: answering an
/// observable-gauge callback with <c>COUNT(*)</c> would put a read on the log file every time a collector
/// ticked, purely because somebody attached a listener. Same reasoning as the jobs and outbox queue depths.
/// </para>
/// </remarks>
public sealed class LogMetrics : IDisposable
{
    /// <summary>The meter name to subscribe to.</summary>
    public const string MeterName = "Rask.Logging";

    private readonly Meter _meter;
    private readonly Counter<long> _written;
    private readonly Counter<long> _dropped;
    private readonly Counter<long> _purged;
    private readonly ObservableGauge<int> _storedGauge;

    private int _stored;

    /// <summary>
    /// Constructs the meter. Prefers the DI-supplied <see cref="IMeterFactory"/> so the instruments join the
    /// host's metrics pipeline and stay isolated between tests; falls back to a standalone
    /// <see cref="Meter"/> when no factory is registered.
    /// </summary>
    public LogMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);

        _written = _meter.CreateCounter<long>(
            "rask.logs.written", "{entry}", "Log entries persisted to the store.");
        _dropped = _meter.CreateCounter<long>(
            "rask.logs.dropped",
            "{entry}",
            "Log entries discarded because the buffer was full. The number worth alerting on: it means the "
            + "stored log is incomplete.");
        _purged = _meter.CreateCounter<long>(
            "rask.logs.purged", "{entry}", "Log entries removed by retention.");

        _storedGauge = _meter.CreateObservableGauge(
            "rask.logs.stored", () => Volatile.Read(ref _stored),
            "{entry}", "Log entries currently held by the store.");
    }

    // Exposed for tests so a MeterListener can scope to this exact meter INSTANCE. Filtering by meter
    // name isn't enough: xUnit runs test classes in parallel, each with its own provider and its own
    // equally-named meter, so a name filter collects other tests' measurements too.
    internal Meter Meter => _meter;

    /// <summary>
    /// <c>true</c> when something is actually collecting <c>rask.logs.stored</c>, so the writer should pay
    /// for the count. <c>false</c> — the normal case — means it shouldn't.
    /// </summary>
    public bool WantsStoredCount => _storedGauge.Enabled;

    /// <summary>Records a flushed batch.</summary>
    public void Written(int count) => _written.Add(count);

    /// <summary>
    /// Records entries that never reached the store — refused by a full buffer, or lost with a batch whose
    /// write failed. Both are the same thing to whoever is reading the log: entries that are not there.
    /// </summary>
    public void Dropped(int count = 1)
    {
        if (count > 0)
        {
            _dropped.Add(count);
        }
    }

    /// <summary>Records rows removed by a retention sweep.</summary>
    public void Purged(int count)
    {
        if (count > 0)
        {
            _purged.Add(count);
        }
    }

    /// <summary>Publishes a stored-count sample taken by the writer's sweep.</summary>
    public void ObserveStored(long stored) => Volatile.Write(ref _stored, (int)Math.Min(stored, int.MaxValue));

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
