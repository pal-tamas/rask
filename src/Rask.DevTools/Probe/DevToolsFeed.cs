namespace Rask.DevTools.Probe;

/// <summary>Which way a frame crossed the wire, from the inspected page's point of view.</summary>
internal enum DevToolsWireDirection : byte
{
    /// <summary>The page sent it to the app: an event, a navigation, a hello.</summary>
    Out,

    /// <summary>The app sent it to the page: a render frame, an ack, a control frame.</summary>
    In,
}

/// <summary>One frame on the wire, as the panel's Wire tab lists it.</summary>
/// <param name="Sequence">Monotonic per feed, so the panel can key rows and tell what is new.</param>
/// <param name="Direction">Which way it went.</param>
/// <param name="Kind">The frame's <c>type</c> for inbound traffic; <c>frame</c> for a render the app sent.</param>
/// <param name="Bytes">Its UTF-8 size on the wire.</param>
/// <param name="Timestamp">A <see cref="System.Diagnostics.Stopwatch" /> timestamp.</param>
/// <param name="DiffOps">For a render frame, how many edit ops it carried; null for a full document or other traffic.</param>
internal readonly record struct DevToolsWireEvent(
    long Sequence,
    DevToolsWireDirection Direction,
    string Kind,
    int Bytes,
    long Timestamp,
    int? DiffOps);

/// <summary>
///     What the devtools have seen of one inspected session, bounded so a long-running page cannot grow it without limit.
/// </summary>
/// <remarks>
///     Written from the inspected session's render and dispatch paths and read from the panel's own session, so every
///     access takes the lock. Traffic is at most a few frames per interaction, which makes a lock cheaper than anything
///     cleverer and the ordering trivially right.
/// </remarks>
internal sealed class DevToolsFeed
{
    /// <summary>How many wire events a feed keeps; the oldest go first.</summary>
    internal const int WireCapacity = 1000;

    private readonly Lock _gate = new();
    private readonly DevToolsWireEvent[] _wire = new DevToolsWireEvent[WireCapacity];
    private int _wireStart;
    private int _wireCount;
    private long _sequence;

    // The op count the last diff produced, attached to the render frame that carries it: DiffComputed fires while the
    // payload is written, and FrameSent right after, on the same session.
    private int? _pendingDiffOps;

    /// <summary>Raised after every recorded event, outside the lock. Subscribers must not block.</summary>
    internal event Action? Changed;

    internal void RecordDiff(int opCount, bool usedDiff)
    {
        lock (_gate)
        {
            _pendingDiffOps = usedDiff ? opCount : null;
        }
    }

    internal void RecordWire(DevToolsWireDirection direction, string kind, int bytes, long timestamp)
    {
        lock (_gate)
        {
            int? ops = null;
            if (direction == DevToolsWireDirection.In && kind == "frame")
            {
                ops = _pendingDiffOps;
                _pendingDiffOps = null;
            }

            var item = new DevToolsWireEvent(++_sequence, direction, kind, bytes, timestamp, ops);
            if (_wireCount < WireCapacity)
            {
                _wire[(_wireStart + _wireCount) % WireCapacity] = item;
                _wireCount++;
            }
            else
            {
                _wire[_wireStart] = item;
                _wireStart = (_wireStart + 1) % WireCapacity;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>The wire events currently held, oldest first.</summary>
    internal DevToolsWireEvent[] WireSnapshot()
    {
        lock (_gate)
        {
            var copy = new DevToolsWireEvent[_wireCount];
            for (var i = 0; i < _wireCount; i++)
            {
                copy[i] = _wire[(_wireStart + i) % WireCapacity];
            }

            return copy;
        }
    }
}
