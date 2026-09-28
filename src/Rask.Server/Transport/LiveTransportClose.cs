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
