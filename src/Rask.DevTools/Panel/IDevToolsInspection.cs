using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     How a panel finds the session it was opened for.
/// </summary>
/// <remarks>
///     Asked on every render of the page that shows the session, never remembered from the request that admitted the
///     panel. A panel is a live page: once admitted, it can navigate itself to another <c>?inspect=</c> over its own
///     socket, and that navigation passes no admission check. So the token and the owner are checked here as well, and a
///     panel that points its query at somebody else's session finds nothing.
/// </remarks>
internal interface IDevToolsInspection
{
    /// <summary>
    ///     The feed of <paramref name="sessionId" />, or null unless <paramref name="token" /> is that session's token and
    ///     the panel's viewer owns it.
    /// </summary>
    DevToolsFeed? Open(string? sessionId, string? token);
}
