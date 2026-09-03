namespace Rask.Meta.Hosting;

/// <summary>
///     Whether the supervised Node process is currently accepting connections.
/// </summary>
/// <remarks>
///     Shared between the supervisor, which sets it, and the forwarder, which refuses to forward
///     before it is set. Without this gate the first request after a container start is forwarded into
///     a closed port and surfaces as a 502 — a real deployment reads as broken for however long the
///     framework takes to boot, which for a cold Node process is seconds, not milliseconds.
/// </remarks>
internal sealed class NodeReadiness
{
    private volatile bool _ready;

    /// <summary>Whether the process is up and listening.</summary>
    internal bool IsReady => _ready;

    /// <summary>Marks the process ready to receive forwarded requests.</summary>
    internal void MarkReady() => _ready = true;

    /// <summary>Marks the process unavailable — it exited, or has not finished starting.</summary>
    internal void MarkNotReady() => _ready = false;
}
