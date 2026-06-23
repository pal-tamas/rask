namespace Rask.Server;

/// <summary>
///     Server-host-only runtime limits — the WebSocket safety caps and session grace periods that
///     only the ASP.NET host has (the WASM runtime has no socket server). Configured through the
///     second callback of <c>AddRask</c>:
///     <code>
///     services.AddRask(
///         live   =&gt; live.DiffMode = LiveDiffMode.Auto,
///         server =&gt; server.MaxInboundFramesPerSecond = 500);
///     </code>
///     Bind from configuration with
///     <c>AddRask(configureServer: o =&gt; builder.Configuration.GetSection("Rask").Bind(o))</c>.
///     Every default matches the framework's prior hardcoded value, so leaving this unconfigured
///     changes nothing.
/// </summary>
public sealed class RaskServerOptions
{
    /// <summary>
    ///     Hard cap (bytes) on a single reassembled inbound WebSocket frame. Client→server messages
    ///     are small (event dispatch / jsResult / navigate) and file uploads use the HTTP endpoint, so
    ///     the 8&nbsp;MB default is generous headroom; it bounds a per-socket memory DoS where a client
    ///     streams an unbounded fragmented frame.
    /// </summary>
    public int MaxInboundFrameBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    ///     Maximum handler dispatches that may be queued (awaiting their turn in WS-arrival order)
    ///     before the server closes the socket with a backpressure policy violation. Each queued
    ///     dispatch holds a cloned payload, so an unbounded queue is a memory DoS. <c>0</c> disables
    ///     the cap. Default 512 — far above any legitimate burst.
    /// </summary>
    public int MaxPendingHandlers { get; set; } = 512;

    /// <summary>
    ///     Maximum inbound WebSocket messages per second on a single connection before the server
    ///     closes the socket, counted over a sliding one-second window. Bounds a small-frame CPU DoS
    ///     the size and backpressure caps miss. <c>0</c> disables the cap. Default 1000 — well above a
    ///     realistic interaction peak; tune ingress with a reverse-proxy rate limiter too.
    /// </summary>
    public int MaxInboundFramesPerSecond { get; set; } = 1000;

    /// <summary>
    ///     How long a session is retained after its WebSocket disconnects, so a reconnecting client
    ///     resumes against the same component tree. Longer survives flakier networks at the cost of
    ///     holding a DI scope + tree per disconnected client. Default 30&nbsp;seconds.
    /// </summary>
    public TimeSpan SessionGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long a session minted by the GET shell is retained before its first WebSocket
    ///     <c>hello</c> arrives. The runtime connects within a second of page load, so a much shorter
    ///     window than <see cref="SessionGracePeriod" /> suffices — and it stops a flood of GETs that
    ///     never open a socket (scanners, prefetchers) from pinning a scope + tree. Default
    ///     10&nbsp;seconds.
    /// </summary>
    public TimeSpan UnconnectedSessionGracePeriod { get; set; } = TimeSpan.FromSeconds(10);
}
