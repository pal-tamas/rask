namespace Rask.Server.Transport;

/// <summary>
///     One live connection as the log tells it: its number, its carrier, when it opened, and why it ended.
/// </summary>
/// <remarks>
///     The end is noted by whichever code ends it — a safety cap, a send timeout, the drain, the receive loop seeing
///     the browser's close — and the first note wins, because what follows a close (the reset the loop then sees) is
///     its consequence, not its cause. Written once and read once, when the connection's cleanup logs it.
/// </remarks>
internal sealed class ConnectionRecord(long number, string transport, long openedAt)
{
    private readonly Lock _gate = new();
    private bool _ended;

    /// <summary>This host's running number for the connection, so its open and close lines can be paired.</summary>
    public long Number { get; } = number;

    /// <summary>"WebSocket" or "HTTP stream".</summary>
    public string Transport { get; } = transport;

    /// <summary>When it opened, on the store's clock.</summary>
    public long OpenedAt { get; } = openedAt;

    /// <summary>When a session attached to it, on the store's clock; zero until one does.</summary>
    public long AttachedAt { get; set; }

    public ClosedBy By { get; private set; } = ClosedBy.Client;

    public string Reason { get; private set; } = "the connection ended";

    /// <summary>Notes a polite close this host sent: a shutdown's is the host going, any other is the host's own.</summary>
    public void Ended(LiveTransportClose close, string description) =>
        Ended(close == LiveTransportClose.GoingAway ? ClosedBy.Shutdown : ClosedBy.Server, description);

    /// <summary>Notes why the connection is ending, unless something already has.</summary>
    public void Ended(ClosedBy by, string reason)
    {
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            By = by;
            Reason = reason;
        }
    }
}
