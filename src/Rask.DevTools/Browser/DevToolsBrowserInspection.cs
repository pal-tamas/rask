using Rask.DevTools.Panel;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Browser;

/// <summary>
///     A WASM panel's way to the session it inspects: the app's own, in the same tab.
/// </summary>
/// <remarks>
///     There is no token or owner to check here, unlike on a Server host. The panel runs in the runtime of the page it
///     inspects, for the one visitor who has that page open, so there is nobody else's session it could reach.
/// </remarks>
internal sealed class DevToolsBrowserInspection(DevToolsFeeds feeds) : IDevToolsInspection
{
    public DevToolsFeed? Open(string? sessionId, string? token) => feeds.Latest;
}
