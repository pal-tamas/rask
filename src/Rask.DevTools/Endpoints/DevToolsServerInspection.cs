using Microsoft.Extensions.DependencyInjection;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Server;
using Rask.Server.Authentication;

namespace Rask.DevTools.Endpoints;

/// <summary>
///     A panel's way into the Server host's sessions. One per panel session, because the viewer it checks is that
///     session's user — the principal the page request set, and the socket's once the panel connects.
/// </summary>
internal sealed class DevToolsServerInspection(
    DevToolsServerEndpoints endpoints,
    LiveSessionStore store,
    DevToolsFeeds feeds,
    SessionUserProvider viewer) : IDevToolsInspection
{
    public DevToolsFeed? Open(string? sessionId, string? token) =>
        endpoints.FindInspected(store, sessionId, token) is { } inspected
        && RaskEndpointExtensions.SameSessionUser(
            viewer.Current, inspected.Services.GetRequiredService<SessionUserProvider>().Current)
            // Created on first use, so a panel that opens before the page has sent anything still follows what comes.
            ? feeds.For(inspected)
            : null;
}
