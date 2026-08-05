using System.Diagnostics.Metrics;

namespace Rask.Mail;

/// <summary>
/// OpenTelemetry-compatible metrics for the mail pillar, published on the <see cref="MeterName"/> meter.
/// Registered as a singleton by <c>AddRaskMail</c> and written to by <see cref="MailProcessor{TContext}"/>.
/// <para>
/// Read locally with <c>dotnet-counters monitor --counters Rask.Mail</c>, or export with
/// <c>MeterProvider.AddMeter(MailMetrics.MeterName)</c>.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately untagged.</b> Jobs and the outbox tag by their registered type — a closed set fixed at
/// build time by a source generator. Mail has no equivalent: the only per-message dimensions are the
/// subject and the recipient, both unbounded, and either would mint a new time series per email sent. That
/// is the standard way to take a metrics backend down, so these count the pillar as a whole.
/// </para>
/// <para>
/// <b>The queue-depth gauges are sampled, not computed on observation</b>, for the same reason as the other
/// pillars: an observable-gauge callback runs on the collector's schedule, and answering it with
/// <c>COUNT(*)</c> would load the database purely because somebody attached a listener.
/// </para>
/// </remarks>
public sealed class MailMetrics : IDisposable
{
    /// <summary>The meter name to subscribe to.</summary>
    public const string MeterName = "Rask.Mail";

    private readonly Meter _meter;
    private readonly Counter<long> _sent;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _deadLettered;
    private readonly Histogram<double> _duration;
    private readonly ObservableGauge<int> _pendingGauge;
    private readonly ObservableGauge<int> _deadLetterGauge;

    private int _pending;
    private int _deadLetters;

    /// <summary>
    /// Constructs the meter. Prefers the DI-supplied <see cref="IMeterFactory"/> so the instruments join the
    /// host's metrics pipeline and stay isolated between tests.
    /// </summary>
    public MailMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);

        _sent = _meter.CreateCounter<long>(
            "rask.mail.sent", "{mail}", "Emails handed to the sender successfully.");
        _failed = _meter.CreateCounter<long>(
            "rask.mail.failed", "{attempt}", "Delivery attempts that threw. Counts every attempt, not every email.");
        _deadLettered = _meter.CreateCounter<long>(
            "rask.mail.deadlettered", "{mail}", "Emails that exhausted MaxAttempts and will not be retried.");
        _duration = _meter.CreateHistogram<double>(
            "rask.mail.duration", "ms", "Wall-clock duration of delivering one email.");

        _pendingGauge = _meter.CreateObservableGauge(
            "rask.mail.pending", () => Volatile.Read(ref _pending),
            "{mail}", "Emails not yet sent and not yet exhausted.");
        _deadLetterGauge = _meter.CreateObservableGauge(
            "rask.mail.deadletters", () => Volatile.Read(ref _deadLetters),
            "{mail}", "Emails that have given up. The number worth alerting on.");
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

    /// <summary>Records an email that was delivered, and how long the send took.</summary>
    public void Sent(double milliseconds)
    {
        _sent.Add(1);
        _duration.Record(milliseconds);
    }

    /// <summary>Records a failed delivery attempt. One email retried five times counts five times here.</summary>
    public void Failed() => _failed.Add(1);

    /// <summary>Records an email crossing into dead-letter state — counted once, on the attempt that exhausts it.</summary>
    public void DeadLettered() => _deadLettered.Add(1);

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
