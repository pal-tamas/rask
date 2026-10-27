using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Server.Transport;

namespace Rask.Server.Diagnostics;

/// <summary>
///     The live-session lifecycle on the <c>Rask.Live</c> category: each connection opening, attaching, closing, and
///     each session made and removed — what an operator needs to tell why a tab reconnected or reloaded.
/// </summary>
/// <remarks>
///     Debug, except a close the server made that the reader sees (Information). Every method asks the logger first,
///     so with the level off a line costs that one call: no fingerprint, no clock read, no string. A session is named
///     by <see cref="SessionFingerprint" />, never by its id, and no line carries a cookie, a claim or a value.
/// </remarks>
internal sealed partial class SessionLifecycleLog(ILogger logger, Func<TimeProvider> clock)
{
    /// <summary>The category every line is written under — the one the rest of the live runtime reports on.</summary>
    public const string Category = "Rask.Live";

    /// <summary>A log that writes nothing, for a store built without a host.</summary>
    public static SessionLifecycleLog None { get; } = new(NullLogger.Instance, static () => TimeProvider.System);

    public void Opened(ConnectionRecord connection)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            ConnectionOpened(logger, connection.Transport, connection.Number);
        }
    }

    public void Attached(ConnectionRecord connection, LiveSession session, AttachKind kind, long waitedSince, long bytesBefore)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var catchUpBytes = session.BytesSent - bytesBefore;
        var catchUp = CatchUp(catchUpBytes, session.LastFrameWasDiff);
        var attach = Describe(kind);
        var waited = Seconds(waitedSince);
        var age = Seconds(session.CreatedAt);
        ConnectionAttached(logger, connection.Transport, connection.Number, session.Fingerprint, attach, waited, age,
            session.Connections, catchUp, catchUpBytes);
    }

    /// <summary>The connection detached from its session, which now waits <paramref name="grace" /> for a reconnect.</summary>
    public void Detached(ConnectionRecord connection, LiveSession session, TimeSpan grace)
    {
        var level = SeenByTheReader(connection.By) ? LogLevel.Information : LogLevel.Debug;
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var who = Who(connection.By);
        var open = Seconds(connection.OpenedAt);
        if (session.Leaving)
        {
            ClosedLeaving(logger, level, connection.Transport, connection.Number, session.Fingerprint, who,
                connection.Reason, open);
            return;
        }

        ClosedDetached(logger, level, connection.Transport, connection.Number, session.Fingerprint, who,
            connection.Reason, open, grace.TotalSeconds);
    }

    /// <summary>The connection closed after a newer one had already taken its session.</summary>
    public void Superseded(ConnectionRecord connection, LiveSession session)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var who = Who(connection.By);
        var open = Seconds(connection.OpenedAt);
        ClosedSuperseded(logger, connection.Transport, connection.Number, session.Fingerprint, who, connection.Reason,
            open);
    }

    /// <summary>The connection closed without ever attaching a session.</summary>
    public void Unattached(ConnectionRecord connection)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var who = Who(connection.By);
        var open = Seconds(connection.OpenedAt);
        ClosedUnattached(logger, connection.Transport, connection.Number, who, connection.Reason, open);
    }

    public void Created(LiveSession session, int count, int max)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            SessionCreated(logger, session.Fingerprint, count, max);
        }
    }

    public void Removed(LiveSession session, SessionRemoval why, TimeSpan limit, int remaining)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        // A session that never had a connection has been without one since it was made.
        var unattachedSince = session.Connections == 0 ? session.CreatedAt : session.DetachedAt;
        var unattached = session.HasOpenTransport ? 0 : Seconds(unattachedSince);
        var reason = Describe(why);
        var age = Seconds(session.CreatedAt);
        SessionRemoved(logger, session.Fingerprint, reason, age, session.Connections, unattached, limit.TotalSeconds,
            remaining);
    }

    public void RefusedAtCapacity(int max)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            AtCapacity(logger, max);
        }
    }

    /// <summary>Why a session went, as the removal line and the refused-attach line both say it.</summary>
    public static string Describe(SessionRemoval why) => why switch
    {
        SessionRemoval.Unconnected =>
            "no connection attached within the unconnected grace period (Rask:Server:UnconnectedSessionGracePeriod)",
        SessionRemoval.Detached =>
            "its connection did not come back within the reconnect grace period (Rask:Server:SessionGracePeriod)",
        SessionRemoval.Left => "its tab left",
        SessionRemoval.Redirected => "its page redirected before anything connected",
        SessionRemoval.Shutdown => "the host is shutting down",
        _ => "the host ended it",
    };

    private static string Describe(AttachKind kind) => kind switch
    {
        AttachKind.New => "its first connection, since its page was served",
        AttachKind.Resume => "a reconnect, since its last connection went",
        AttachKind.Replace => "a reconnect replacing a connection still open, since this one opened",
        _ => "rebuilt from the tab's resume record, since this connection opened",
    };

    private static string Who(ClosedBy by) => by switch
    {
        ClosedBy.Client => "client",
        ClosedBy.Server => "server",
        ClosedBy.Timeout => "timeout",
        ClosedBy.Error => "error",
        _ => "shutdown",
    };

    // The server ended a connection the reader was using: they see "Reconnecting…", and this line says why.
    private static bool SeenByTheReader(ClosedBy by) => by is ClosedBy.Server or ClosedBy.Timeout;

    private static string CatchUp(long bytes, bool diff)
    {
        if (bytes == 0)
        {
            return "none";
        }

        return diff ? "diff" : "whole page";
    }

    private double Seconds(long since) => since == 0 ? 0 : clock().GetElapsedTime(since).TotalSeconds;

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "Rask Live: {Transport} connection {Connection} opened.")]
    private static partial void ConnectionOpened(ILogger logger, string transport, long connection);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Debug, Message =
        "Rask Live: {Transport} connection {Connection} attached to session #{Session}: {Attach} {Waited:0.000} s; "
        + "session age {Age:0.0} s, connection {Attachment} of the session; catch-up: {CatchUp}, {CatchUpBytes} B.")]
    private static partial void ConnectionAttached(ILogger logger, string transport, long connection, string session,
        string attach, double waited, double age, int attachment, string catchUp, long catchUpBytes);

    [LoggerMessage(EventId = 1103, Message =
        "Rask Live: {Transport} connection {Connection} to session #{Session} closed by the {ClosedBy} ({Reason}) "
        + "after {Open:0.0} s; the session is kept {Grace:0.#} s for a reconnect.")]
    private static partial void ClosedDetached(ILogger logger, LogLevel level, string transport, long connection,
        string session, string closedBy, string reason, double open, double grace);

    [LoggerMessage(EventId = 1104, Message =
        "Rask Live: {Transport} connection {Connection} to session #{Session} closed by the {ClosedBy} ({Reason}) "
        + "after {Open:0.0} s; its tab left, so the session goes now.")]
    private static partial void ClosedLeaving(ILogger logger, LogLevel level, string transport, long connection,
        string session, string closedBy, string reason, double open);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Debug, Message =
        "Rask Live: {Transport} connection {Connection} to session #{Session} closed by the {ClosedBy} ({Reason}) "
        + "after {Open:0.0} s; a newer connection already has the session.")]
    private static partial void ClosedSuperseded(ILogger logger, string transport, long connection, string session,
        string closedBy, string reason, double open);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Debug, Message =
        "Rask Live: {Transport} connection {Connection} closed by the {ClosedBy} ({Reason}) after {Open:0.0} s "
        + "without attaching a session.")]
    private static partial void ClosedUnattached(ILogger logger, string transport, long connection, string closedBy,
        string reason, double open);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Debug, Message =
        "Rask Live: session #{Session} created; {Count} session(s) on this host, limit {Max} (0 = none).")]
    private static partial void SessionCreated(ILogger logger, string session, int count, int max);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Debug, Message =
        "Rask Live: session #{Session} removed: {Why}; age {Age:0.0} s, {Connections} connection(s), without one for "
        + "{Unattached:0.0} s, limit {Limit:0.#} s; {Remaining} session(s) left.")]
    private static partial void SessionRemoved(ILogger logger, string session, string why, double age, int connections,
        double unattached, double limit, int remaining);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Information, Message =
        "Rask Live: refused a new session: this host holds its limit of {Max} (Rask:Live:MaxSessions), so the page "
        + "was answered 503.")]
    private static partial void AtCapacity(ILogger logger, int max);
}
