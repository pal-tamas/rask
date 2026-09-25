using System.Net;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;
using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests;

/// <summary>
///     An app page served by a Development host with the devtools attached, live on a socket — what a panel inspects.
/// </summary>
internal static partial class DevToolsLivePage
{
    /// <summary>A Development host for <typeparamref name="TApp" />, with every request from loopback.</summary>
    internal static RaskTestHost Host<TApp>() where TApp : Component =>
        RaskTestHost.Create<TApp>(
            configureServices: s => RaskDevToolsLoader.Attach(s),
            configureMiddleware: app => app.Use((ctx, next) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
                return next(ctx);
            }),
            environment: "Development");

    /// <summary>The app's page, live on a socket: the session, its feed, the socket, and the page's first click handler.</summary>
    internal static async Task<(LiveSessionBase Session, DevToolsFeed Feed, WebSocket Socket, string HandlerId)> OpenAsync(
        RaskTestHost host)
    {
        var (session, feed, socket, handlerIds) = await OpenWithHandlersAsync(host);
        return (session, feed, socket, handlerIds[0]);
    }

    /// <summary>The app's page, live on a socket, with every click handler on it in page order.</summary>
    internal static async Task<(LiveSessionBase Session, DevToolsFeed Feed, WebSocket Socket, IReadOnlyList<string> HandlerIds)>
        OpenWithHandlersAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/");
        var sessionId = SessionId().Match(html).Groups[1].Value;
        var handlerIds = HandlerId().Matches(html).Select(m => m.Groups[1].Value).ToList();
        Assert.True(handlerIds.Count > 0, "the page has no click handler:" + Environment.NewLine + html);

        var session = host.Store.Get(sessionId);
        Assert.NotNull(session);
        var feed = host.Services.GetRequiredService<DevToolsFeeds>().For(session);

        var socket = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await socket.SendJsonAsync(new { type = "hello", session = sessionId });
        while (await socket.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300)) is not null)
        {
        }

        return (session, feed, socket, handlerIds);
    }

    /// <summary>Polls until <paramref name="until" /> holds, so a test says what it is waiting for rather than how long.</summary>
    internal static async Task<bool> WaitFor(Func<bool> until, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (until())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return until();
    }

    [GeneratedRegex("data-rask-root=\"([^\"]+)\"")]
    private static partial Regex SessionId();

    [GeneratedRegex("data-rask-on-click=\"([^\"]+)\"")]
    private static partial Regex HandlerId();
}
