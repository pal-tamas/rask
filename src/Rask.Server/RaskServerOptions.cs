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
///         Each host carries its own limits: <c>AddRask</c> projects these values into a per-host
///         <c>RaskServerLimits</c> singleton that the WebSocket endpoint resolves once per connection,
///         so two hosts in one process (or parallel tests) do not share this state.
///     </para>
/// </summary>
public sealed class RaskServerOptions
{
    /// <summary>
    ///     Hard cap (bytes) on a single reassembled inbound WebSocket frame. Client→server messages
    ///     are small (event dispatch / jsResult / navigate) and file uploads use the HTTP endpoint, so
    ///     the 8&#160;MB default is generous headroom; it bounds a per-socket memory DoS where a client
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
    ///     holding a DI scope + tree per disconnected client. Default 30&#160;seconds.
    /// </summary>
    public TimeSpan SessionGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long a session minted by the GET shell is retained before its first WebSocket
    ///     <c>hello</c> arrives. The runtime connects within a second of page load, so a much shorter
    ///     window than <see cref="SessionGracePeriod" /> suffices — and it stops a flood of GETs that
    ///     never open a socket (scanners, prefetchers) from pinning a scope + tree. Default
    ///     10&#160;seconds.
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
    ///     How long a single event-handler dispatch may run before its cancellation token
    ///     (<c>Component.CancellationToken</c>) is cancelled. A handler that threads that token
    ///     into its async work (an <c>HttpClient</c> call, a <c>Task.Delay</c>) then unwinds cleanly
    ///     instead of pinning the session's render pipeline; the timeout is logged and metered. It is
    ///     cooperative — a handler that ignores the token cannot be force-aborted (the backpressure and
    ///     idle-socket caps remain the backstop for that). <see cref="TimeSpan.Zero" /> (default)
    ///     disables the timeout.
    /// </summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Maximum aggregate bytes of inbound handler payloads queued (awaiting their turn in
    ///     WS-arrival order) before the server closes the socket with a backpressure policy violation.
    ///     Complements <see cref="MaxPendingHandlers" /> (which bounds the queue's <em>count</em>): a
    ///     client could fill the count-bounded queue with large frames and pin many megabytes of cloned
    ///     payloads, so this bounds the queue's <em>memory</em>. Measured by inbound wire bytes — a close
    ///     proxy for the cloned <c>JsonElement</c> footprint, sized to bound the same order of magnitude
    ///     rather than the exact managed size. <c>0</c> (default) disables the cap.
    /// </summary>
    public long MaxPendingHandlerBytes { get; set; }

    /// <summary>
    ///     How long a single outbound frame may take before the socket is aborted. Default 30 s;
    ///     <c>TimeSpan.Zero</c> disables the cap.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>WebSocket.SendAsync</c> completes when the frame reaches the transport, not when the
    ///         client reads it — so a client that simply stops reading fills the send buffer and the send
    ///         never returns. Sends happen under the session's render lock, which also guards its teardown,
    ///         so without this one unresponsive client pins its own session indefinitely: no further
    ///         renders, and a <c>Dispose</c> that cannot complete.
    ///     </para>
    ///     <para>
    ///         On timeout the socket is aborted rather than the session discarded. The session survives for
    ///         <see cref="SessionGracePeriod" /> exactly as it would after any other drop, so a client on a
    ///         briefly-stalled link reconnects to the page it had.
    ///     </para>
    ///     <para>
    ///         The default is deliberately generous: this is a stuck-connection backstop, not a latency
    ///         budget. A slow mobile link should never trip it.
    ///     </para>
    /// </remarks>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Whether a client may rebuild its page on a host that has never heard of its session — after a
    ///     restart, a redeploy, or (later) a reconnect routed to another node. <c>true</c> by default.
    ///     <para>
    ///         A live session cannot be moved or saved: it is a component tree, a DI scope and a set of
    ///         cancellation tokens. So this does not resume anything. The server hands the client an
    ///         encrypted record of the page's URL and whatever the app declared through
    ///         <see cref="Rask.Core.Live.IPersistentState" />, and a host that receives it back
    ///         <em>rebuilds</em> the page around it. Undeclared state is gone either way — but the user
    ///         gets their page back instead of the "session timed out, reload" they get today.
    ///     </para>
    ///     <para>
    ///         Worth having even for an app that declares nothing: the URL alone turns a deploy's
    ///         full-page reload into a re-render. Turn it off to keep the reload — the record rides the
    ///         socket, so an app whose pages are expensive to build may prefer the reload's clean slate.
    ///     </para>
    /// </summary>
    public bool SessionResume { get; set; } = true;

    /// <summary>
    ///     How long a resume record stays redeemable. Default 1 hour.
    ///     <para>
    ///         This is not the reconnect grace period (<see cref="SessionGracePeriod" />, 30 s), which
    ///         covers a blip against the <em>intact</em> session. This covers the session being gone:
    ///         a laptop that slept through a deploy, a tunnel that dropped for twenty minutes. The record
    ///         is encrypted and signed, bound to the principal it was issued for, and lives in the tab's
    ///         <c>sessionStorage</c>, so it dies with the tab regardless.
    ///     </para>
    ///     <para>
    ///         Enforced by ASP.NET's time-limited data protector rather than a field we check ourselves,
    ///         so an expired record fails to unprotect at all.
    ///     </para>
    /// </summary>
    public TimeSpan ResumeTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Budget for the graceful shutdown drain: announcing the shutdown to every connected browser,
    ///     letting in-flight event handlers finish, closing each WebSocket with a real handshake (status
    ///     1001, "going away") and disposing the sessions. Sockets still open when it elapses are aborted.
    ///     <para>
    ///         Must fit inside <c>HostOptions.ShutdownTimeout</c>, which in turn must fit inside whatever
    ///         your container runtime allows between <c>SIGTERM</c> and <c>SIGKILL</c> (<c>rask deploy</c>
    ///         uses 20&#160;seconds). Note that <c>HostOptions.ServicesStopConcurrently</c> is <c>false</c>
    ///         by default, so other hosted services' shutdown work is spent from the same budget before
    ///         this drain is even entered.
    ///     </para>
    ///     <para>
    ///         <see cref="TimeSpan.Zero" /> disables the drain and restores the previous behaviour —
    ///         every socket aborted the moment shutdown begins, which the browser sees as an abnormal
    ///         (1006) closure. Default 5&#160;seconds; a drain normally completes in one round trip.
    ///     </para>
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

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

        if (HandlerTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HandlerTimeout), HandlerTimeout,
                "HandlerTimeout must be >= TimeSpan.Zero (Zero disables the timeout).");
        }

        if (MaxPendingHandlerBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingHandlerBytes), MaxPendingHandlerBytes,
                "MaxPendingHandlerBytes must be >= 0 (0 disables the cap).");
        }

        if (SendTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SendTimeout), SendTimeout,
                "SendTimeout must be >= TimeSpan.Zero (Zero disables the timeout).");
        }

        // Zero is not "off" here — SessionResume is. A zero or negative lifetime would mint records that
        // are already expired, so every reconnect would pay to build one and then be refused it.
        if (SessionResume && ResumeTokenLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ResumeTokenLifetime), ResumeTokenLifetime,
                "ResumeTokenLifetime must be positive. Set SessionResume = false to disable resume.");
        }

        if (ShutdownDrainTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownDrainTimeout), ShutdownDrainTimeout,
                "ShutdownDrainTimeout must be >= TimeSpan.Zero (Zero disables the drain).");
        }

        // CancellationTokenSource.CancelAfter throws above int.MaxValue milliseconds, and it would throw
        // from the shutdown path — the worst possible place to discover a bad value.
        if (ShutdownDrainTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownDrainTimeout), ShutdownDrainTimeout,
                $"ShutdownDrainTimeout must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }
    }
}
