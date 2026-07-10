namespace Rask.Server;

/// <summary>
///     Per-host snapshot of the WebSocket receive-loop and session-lifecycle safety limits — the
///     DoS-protection caps (inbound frame size / rate, pending-handler count &amp; bytes, handler and
///     idle-socket timeouts) and the reconnect grace periods. Seeded from <see cref="RaskServerOptions" />
///     when <c>AddRask</c> runs and registered as a singleton; the WebSocket endpoint resolves it once
///     per connection (and the GET shell once per request), then the receive loop reads it via instance
///     fields on the hot path. This replaces the former process-global mutable statics, so two hosts in
///     one process — and parallel tests — each carry their own limits instead of clobbering shared state.
///     Mirrors the per-store <c>LiveSessionStore.MaxSessions</c> pattern.
/// </summary>
internal sealed class RaskServerLimits
{
    /// <summary>Hard cap (bytes) on a single reassembled inbound WebSocket frame. See <see cref="RaskServerOptions.MaxInboundFrameBytes" />.</summary>
    public int MaxInboundFrameBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Max handler dispatches queued before the backpressure breaker closes the socket. 0 = off.</summary>
    public int MaxPendingHandlers { get; init; } = 512;

    /// <summary>Max inbound WS messages/second over a sliding window before the socket is closed. 0 = off.</summary>
    public int MaxInboundFramesPerSecond { get; init; } = 1000;

    /// <summary>How long a disconnected session is retained for reconnect against the same tree.</summary>
    public TimeSpan SessionGracePeriod { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Grace for a GET-minted session that has not yet sent its first WS <c>hello</c> (probe defence).</summary>
    public TimeSpan UnconnectedSessionGracePeriod { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Close a connected socket that sends no inbound frame for this long. Zero = off.</summary>
    public TimeSpan IdleSocketTimeout { get; init; } = TimeSpan.Zero;

    /// <summary>Cancel a handler dispatch's token after this long (cooperative). Zero = off.</summary>
    public TimeSpan HandlerTimeout { get; init; } = TimeSpan.Zero;

    /// <summary>Aggregate queued cloned-payload bytes before the backpressure breaker closes the socket. 0 = off.</summary>
    public long MaxPendingHandlerBytes { get; init; }

    /// <summary>Projects a validated <see cref="RaskServerOptions" /> into the per-host limit snapshot.</summary>
    public static RaskServerLimits From(RaskServerOptions o) => new()
    {
        MaxInboundFrameBytes = o.MaxInboundFrameBytes,
        MaxPendingHandlers = o.MaxPendingHandlers,
        MaxInboundFramesPerSecond = o.MaxInboundFramesPerSecond,
        SessionGracePeriod = o.SessionGracePeriod,
        UnconnectedSessionGracePeriod = o.UnconnectedSessionGracePeriod,
        IdleSocketTimeout = o.IdleSocketTimeout,
        HandlerTimeout = o.HandlerTimeout,
        MaxPendingHandlerBytes = o.MaxPendingHandlerBytes,
    };
}
