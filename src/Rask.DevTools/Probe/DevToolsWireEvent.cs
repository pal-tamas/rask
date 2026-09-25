namespace Rask.DevTools.Probe;

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
