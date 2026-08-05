using System.Diagnostics.Metrics;

namespace Rask.Server.Diagnostics;

/// <summary>
///     The Rask server's OpenTelemetry-compatible metrics, published on the
///     <see cref="RaskTelemetry.MeterName" /> meter. Registered as a singleton by
///     <c>AddRask()</c> and threaded into the instrumentation points (session lifecycle in
///     <see cref="LiveSessionStore" />, frame-rejection and handler dispatch in the WebSocket
///     loop). Every instrument is allocation-light on the hot path: counters take a single
///     <c>Add(1)</c>, the active-sessions reading is an observable gauge polled by the collector.
///     <para>
///         Subscribe with <c>dotnet-counters monitor --counters Rask.Server</c> or an OpenTelemetry
///         <c>MeterProvider.AddMeter(RaskTelemetry.MeterName)</c>.
///     </para>
/// </summary>
public sealed class RaskMetrics : IDisposable
{
    private readonly Counter<long> _framesRejected;
    private readonly Counter<long> _sessionsResumed;
    private readonly Counter<long> _sessionsResumeRejected;
    private readonly Counter<long> _handlersDispatched;
    private readonly Counter<long> _handlersFaulted;
    private readonly Counter<long> _handlersTimedOut;
    private readonly Histogram<double> _handlerDuration;
    private readonly Meter _meter;
    private readonly Counter<long> _sessionsAbandonedAtDrain;
    private readonly Counter<long> _sessionsCreated;
    private readonly Counter<long> _sessionsEvicted;
    private readonly Counter<long> _sessionsRejected;

    /// <summary>
    ///     Constructs the meter. Prefers the DI-supplied <see cref="IMeterFactory" /> (so the
    ///     instruments participate in the host's metrics pipeline and test isolation); falls back to a
    ///     standalone <see cref="Meter" /> when no factory is registered.
    /// </summary>
    public RaskMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(RaskTelemetry.MeterName)
                 ?? new Meter(RaskTelemetry.MeterName);

        _sessionsCreated = _meter.CreateCounter<long>(
            "rask.sessions.created", "{session}", "Live sessions created (component tree + DI scope).");
        _sessionsRejected = _meter.CreateCounter<long>(
            "rask.sessions.rejected", "{session}", "Session creations refused because MaxSessions was reached.");
        _sessionsEvicted = _meter.CreateCounter<long>(
            "rask.sessions.evicted", "{session}", "Live sessions removed (disconnect grace elapsed or shutdown).");
        _handlersDispatched = _meter.CreateCounter<long>(
            "rask.handlers.dispatched", "{handler}", "Client event handlers dispatched to user code.");
        _handlersFaulted = _meter.CreateCounter<long>(
            "rask.handlers.faulted", "{handler}", "Event-handler dispatches that threw (isolated, session survives).");
        _handlersTimedOut = _meter.CreateCounter<long>(
            "rask.handlers.timedout", "{handler}", "Event-handler dispatches cancelled by the HandlerTimeout.");
        _handlerDuration = _meter.CreateHistogram<double>(
            "rask.handler.duration", "ms", "Wall-clock duration of an event-handler dispatch.");
        _framesRejected = _meter.CreateCounter<long>(
            "rask.ws.frames.rejected", "{frame}", "Inbound WebSocket frames rejected by a safety limit.");
        _sessionsResumed = _meter.CreateCounter<long>(
            "rask.sessions.resumed", "{session}", "Sessions rebuilt from a client's resume record.");
        _sessionsResumeRejected = _meter.CreateCounter<long>(
            "rask.sessions.resume_rejected", "{session}", "Resume records refused, tagged with the reason.");
        _sessionsAbandonedAtDrain = _meter.CreateCounter<long>(
            "rask.shutdown.sessions.abandoned", "{session}",
            "Live sessions still connected when the shutdown drain budget ran out (their sockets were aborted).");
    }

    // Exposed for tests so a MeterListener can scope to this exact meter instance and ignore
    // measurements from any other RaskMetrics constructed concurrently.
    internal Meter Meter => _meter;

    public void Dispose() => _meter.Dispose();

    /// <summary>Registers the observable active-session gauge backed by <paramref name="readCount" />.</summary>
    public void TrackActiveSessions(Func<int> readCount) =>
        _meter.CreateObservableGauge(
            "rask.sessions.active", readCount, "{session}", "Live sessions currently held by the store.");

    public void SessionCreated() => _sessionsCreated.Add(1);

    public void SessionRejected() => _sessionsRejected.Add(1);

    public void SessionEvicted() => _sessionsEvicted.Add(1);

    /// <summary>
    ///     Records sessions whose sockets had to be aborted because the drain budget ran out. A nonzero
    ///     reading is the signal that <c>RaskServerOptions.ShutdownDrainTimeout</c> (or the
    ///     <c>HostOptions.ShutdownTimeout</c> containing it) is shorter than the app's real shutdown.
    /// </summary>
    public void SessionsAbandonedAtDrain(int count) => _sessionsAbandonedAtDrain.Add(count);

    public void HandlerDispatched() => _handlersDispatched.Add(1);

    public void HandlerFaulted() => _handlersFaulted.Add(1);

    public void HandlerTimedOut() => _handlersTimedOut.Add(1);

    public void RecordHandlerDuration(double milliseconds) => _handlerDuration.Record(milliseconds);

    /// <summary>
    ///     Counts an inbound frame/socket the receive loop refused, tagged with the limit that tripped:
    ///     <c>size</c> (frame exceeded the byte cap), <c>rate</c> (frame-per-second flood),
    ///     <c>backlog</c> (pending-handler queue overflow), or <c>idle</c> (idle-socket timeout).
    /// </summary>
    public void FrameRejected(string reason) =>
        _framesRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Counts a page rebuilt on a host that had never heard of the session, from the client's record.</summary>
    public void SessionResumed() => _sessionsResumed.Add(1);

    /// <summary>
    ///     Counts a resume record the host refused, tagged with why: <c>malformed</c> (not a record this
    ///     code wrote), <c>unprotect</c> (tampered, expired, or sealed under a key ring this host does not
    ///     have — the last of which is what an unpersisted key ring looks like after a redeploy),
    ///     <c>principal</c> (issued to a different user), <c>toolarge</c>, or <c>atcapacity</c>
    ///     (<c>MaxSessions</c> reached, so the rebuild shed like any other new session).
    ///     <para>
    ///         A steady trickle is normal — expired records from closed laptops. A spike on
    ///         <c>unprotect</c> right after a deploy means the key ring is not surviving the deploy.
    ///     </para>
    /// </summary>
    public void ResumeRejected(string reason) =>
        _sessionsResumeRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
