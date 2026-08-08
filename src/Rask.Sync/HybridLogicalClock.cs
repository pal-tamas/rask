namespace Rask.Sync;

/// <summary>
///     Issues <see cref="HlcTimestamp" />s that order events across devices that do not share a clock.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why not <see cref="DateTimeOffset.UtcNow" />.</b> Ordering offline edits by wall clock is
///         the classic way to lose data: device clocks disagree by minutes, users set them by hand, and
///         they run backwards over NTP corrections and daylight-saving changes. An edit made later can
///         carry an earlier timestamp, so last-writer-wins silently discards the newer value and nothing
///         anywhere reports a problem.
///     </para>
///     <para>
///         A hybrid logical clock keeps wall-clock time as its physical component so stamps stay roughly
///         meaningful to a human, but it never moves backwards and it advances on every message it sees.
///         Once a node observes a remote stamp, everything it issues afterwards sorts after that stamp —
///         so an edit made in response to something you received always wins over it, whatever the two
///         devices believe the time to be.
///     </para>
///     <para>
///         This does <b>not</b> detect concurrency. It gives a total order, not causality; two genuinely
///         simultaneous edits are ordered arbitrarily but <em>consistently</em>, which is what makes the
///         merge deterministic. Telling a user their value was overwritten is
///         <see cref="SyncConflict" />'s job, not the clock's.
///     </para>
///     <para>Instances are thread-safe.</para>
/// </remarks>
public sealed class HybridLogicalClock
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private int _counter;
    private long _physicalMs;

    /// <summary>Creates a clock for <paramref name="nodeId" />, reading wall time from <paramref name="timeProvider" />.</summary>
    /// <param name="nodeId">
    ///     This device's identity. Must be stable for the device and distinct from every other node —
    ///     it is the final tie-break that keeps the order total. A per-install <see cref="Guid" /> is the
    ///     usual choice.
    /// </param>
    /// <param name="timeProvider">Wall clock; defaults to <see cref="TimeProvider.System" />.</param>
    public HybridLogicalClock(string nodeId, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        NodeId = nodeId;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>This clock's node identity.</summary>
    public string NodeId { get; }

    /// <summary>The last stamp this clock issued or observed.</summary>
    public HlcTimestamp Current
    {
        get
        {
            lock (_gate)
            {
                return new HlcTimestamp(_physicalMs, _counter, NodeId);
            }
        }
    }

    /// <summary>
    ///     Issues the stamp for a local event. Strictly greater than every stamp this clock has issued or
    ///     observed, even if the wall clock has not moved or has moved backwards.
    /// </summary>
    public HlcTimestamp Tick()
    {
        lock (_gate)
        {
            var wall = _time.GetUtcNow().ToUnixTimeMilliseconds();

            if (wall > _physicalMs)
            {
                // Time moved on: adopt it and restart the counter.
                _physicalMs = wall;
                _counter = 0;
            }
            else
            {
                // The wall clock has stalled within a millisecond, or gone backwards. Either way the
                // logical component carries the ordering so the stamp still advances.
                _counter++;
            }

            return new HlcTimestamp(_physicalMs, _counter, NodeId);
        }
    }

    /// <summary>
    ///     Advances this clock past <paramref name="remote" />, then issues a local stamp. Call this when
    ///     receiving an op so that anything issued afterwards sorts after what was received.
    /// </summary>
    public HlcTimestamp Observe(HlcTimestamp remote)
    {
        lock (_gate)
        {
            var wall = _time.GetUtcNow().ToUnixTimeMilliseconds();
            var physical = Math.Max(Math.Max(_physicalMs, remote.PhysicalMs), wall);

            _counter = physical == _physicalMs && physical == remote.PhysicalMs
                // Both clocks are in the same millisecond: continue past whichever counter is further on.
                ? Math.Max(_counter, remote.Counter) + 1
                : physical == _physicalMs
                    ? _counter + 1
                    : physical == remote.PhysicalMs
                        ? remote.Counter + 1
                        // Wall time is ahead of both: it alone carries the ordering.
                        : 0;

            _physicalMs = physical;
            return new HlcTimestamp(_physicalMs, _counter, NodeId);
        }
    }
}
