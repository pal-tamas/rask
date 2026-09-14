namespace Rask.Server.Transport;

/// <summary>
///     Why a live connection is being closed.
/// </summary>
internal enum LiveTransportClose
{
    /// <summary>The connection did its job and is finished with.</summary>
    Normal,

    /// <summary>The host is shutting down; the client should come back to whatever serves next.</summary>
    GoingAway,

    /// <summary>The client broke a safety cap — the rate, size or backlog breakers.</summary>
    PolicyViolation,
}

/// <summary>
///     The one client connection a live session writes its frames to.
/// </summary>
/// <remarks>
///     <para>
///         A session renders and hands bytes to whatever is carrying them. Today that is a WebSocket
///         (<see cref="WebSocketTransport" />); the point of naming the seam is that a second carrier can
///         exist without the session, the render pipeline or the frame processor knowing which one they
///         are talking to.
///     </para>
///     <para>
///         <b>One writer at a time.</b> Implementations are not required to be safe for concurrent sends;
///         <c>LiveSession</c> serialises every write behind its render lock, which is also what keeps a
///         close frame from interleaving a render's send.
///     </para>
/// </remarks>
internal interface ILiveTransport
{
    /// <summary>
    ///     Whether this connection can still carry a frame. A session checks it before rendering, so a
    ///     dropped client costs a state check rather than a render walk.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>Writes one complete frame.</summary>
    /// <remarks>
    ///     Completing means the frame reached the transport, never that the client read it — which is why
    ///     <c>LiveSession.SendGuardedAsync</c> puts a timeout around this call and aborts a client that has
    ///     stopped reading.
    /// </remarks>
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct);

    /// <summary>
    ///     Ends the connection politely, telling the client why.
    /// </summary>
    /// <remarks>
    ///     Send-only by contract, and the drain depends on that: it closes while the connection's reader is
    ///     parked mid-receive, so an implementation that also waited for the peer's echo would be a second
    ///     concurrent read. The reader observes the peer's reply through its own loop and unwinds there.
    /// </remarks>
    Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct);

    /// <summary>
    ///     Drops the connection without a handshake, for when a polite close cannot be waited for — a send
    ///     that timed out, or the drain's hard deadline. Never throws.
    /// </summary>
    void Abort();
}
