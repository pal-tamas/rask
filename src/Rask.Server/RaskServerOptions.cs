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
///     changes nothing. <c>AddRask</c> validates the values and throws
///     <see cref="ArgumentOutOfRangeException" /> on an out-of-range one (a negative grace period, a
///     non-positive frame-size cap), so a misconfiguration fails fast at startup rather than at runtime.
///     <para>
///         These are applied to process-global state read by the WebSocket receive loop, so in a
///         multi-host process the last <c>configureServer</c> wins for every host (the framework's
///         original constants were likewise process-wide). Per-app limits would need a per-connection
///         options lookup; configure one set of limits per process.
///     </para>
/// </summary>
public sealed class RaskServerOptions
{
    /// <summary>
    ///     Hard cap (bytes) on a single reassembled inbound WebSocket frame. Client→server messages
    ///     are small (event dispatch / jsResult / navigate) and file uploads use the HTTP endpoint, so
    ///     the 8&nbsp;MB default is generous headroom; it bounds a per-socket memory DoS where a client
    ///     streams an unbounded fragmented frame. Must be positive — a frame-size cap is mandatory, so
    ///     unlike the other caps there is no <c>0 = off</c>.
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

    /// <summary>
    ///     If a connected WebSocket sends no inbound frame for this long, the server closes it. The
    ///     session itself survives under <see cref="SessionGracePeriod" /> for reconnect, so this only
    ///     reclaims the idle socket (and its receive loop), not the component tree. Bounds a silently
    ///     connected client that would otherwise hold a socket open indefinitely.
    ///     <see cref="TimeSpan.Zero" /> (default) disables the timeout, preserving prior behaviour.
    /// </summary>
    public TimeSpan IdleSocketTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Maximum aggregate bytes of inbound handler payloads queued (awaiting their turn in
    ///     WS-arrival order) before the server closes the socket with a backpressure policy violation.
    ///     Complements <see cref="MaxPendingHandlers" /> (which bounds the queue's <em>count</em>): a
    ///     client could fill the count-bounded queue with large frames and pin many megabytes of cloned
    ///     payloads, so this bounds the queue's <em>memory</em>. <c>0</c> (default) disables the cap.
    /// </summary>
    public long MaxPendingHandlerBytes { get; set; }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> if any value is out of range. Called by
    ///     <c>AddRask</c> after the caller's <c>configureServer</c> runs, so a bad value (a negative
    ///     grace period that would crash <c>Task.Delay</c> and leak the session, a non-positive
    ///     frame-size cap that would abort every socket) surfaces at startup instead of at runtime.
    /// </summary>
    internal void Validate()
    {
        if (MaxInboundFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxInboundFrameBytes), MaxInboundFrameBytes,
                "MaxInboundFrameBytes must be positive — a frame-size cap is mandatory.");
        }

        if (MaxPendingHandlers < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingHandlers), MaxPendingHandlers,
                "MaxPendingHandlers must be >= 0 (0 disables the cap).");
        }

        if (MaxInboundFramesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxInboundFramesPerSecond), MaxInboundFramesPerSecond,
                "MaxInboundFramesPerSecond must be >= 0 (0 disables the cap).");
        }

        if (SessionGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SessionGracePeriod), SessionGracePeriod,
                "SessionGracePeriod must be positive.");
        }

        if (UnconnectedSessionGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UnconnectedSessionGracePeriod), UnconnectedSessionGracePeriod,
                "UnconnectedSessionGracePeriod must be positive.");
        }

        if (IdleSocketTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(IdleSocketTimeout), IdleSocketTimeout,
                "IdleSocketTimeout must be >= TimeSpan.Zero (Zero disables the timeout).");
        }

        if (MaxPendingHandlerBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingHandlerBytes), MaxPendingHandlerBytes,
                "MaxPendingHandlerBytes must be >= 0 (0 disables the cap).");
        }
    }
}
