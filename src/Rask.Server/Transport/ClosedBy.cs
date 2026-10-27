namespace Rask.Server.Transport;

/// <summary>Which side ended a live connection — the first thing an operator asks of a reconnect.</summary>
internal enum ClosedBy
{
    /// <summary>The browser closed it, or went away (a reload, a closed tab, a dropped network).</summary>
    Client,

    /// <summary>This host closed it on purpose: a safety cap, a refused hello, a session it does not know.</summary>
    Server,

    /// <summary>A timer ran out: the idle-socket timeout, or a send the client stopped reading.</summary>
    Timeout,

    /// <summary>The connection failed under both sides: a reset, or a close without its handshake.</summary>
    Error,

    /// <summary>The host is shutting down.</summary>
    Shutdown,
}
