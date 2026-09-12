using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;
using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The component tree the panel shows: taken at the end of the inspected page's render walk, and only while a tab is
///     asking for one.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed partial class DevToolsTreeTabTests
{
    [Fact]
    public async Task A_page_that_renders_while_a_tab_watches_hands_it_a_tree()
    {
        using var host = Host();
        var (session, feed, socket, handlerId) = await LivePage(host);
        using var watch = feed.WatchTree();

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });

        // First that the page actually dispatched: a tree can only come from a render, and the wire feed says whether one
        // was asked for at all.
        var dispatched = await WaitFor(
            () => feed.WireSnapshot().Any(e => e.Kind == "click"), TimeSpan.FromSeconds(5));
        Assert.True(dispatched, "the page never received the click, so it never re-rendered");

        var tree = await WaitForTree(feed, TimeSpan.FromSeconds(5));
        Assert.NotNull(tree);
        // The app's own component, somewhere under the root the host wraps it in.
        Assert.Contains(nameof(DevToolsTestApp), Types(tree!));
        socket.Dispose();
    }

    [Fact]
    public async Task With_no_tab_watching_the_page_is_not_walked_for_a_tree()
    {
        using var host = Host();
        var (_, feed, socket, handlerId) = await LivePage(host);

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        await Task.Delay(500);

        Assert.Null(feed.TreeSnapshot());
        socket.Dispose();
    }

    [Fact]
    public async Task A_component_keeps_its_id_across_renders()
    {
        using var host = Host();
        var (_, feed, socket, handlerId) = await LivePage(host);
        using var watch = feed.WatchTree();

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        var first = await WaitForTree(feed, TimeSpan.FromSeconds(5));
        Assert.NotNull(first);

        // The shape the client really sends for a click, so this drives the page's own dispatch path.
        await socket.SendJsonAsync(new { id = handlerId, type = "click" });
        await Task.Delay(300);
        var second = feed.TreeSnapshot();

        // The id is what a panel keys its expanded branches on, so it must outlive the render it was taken in.
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
        socket.Dispose();
    }

    private static IEnumerable<string> Types(DevToolsComponentNode node)
    {
        yield return node.Type;
        foreach (var type in node.Children.SelectMany(Types))
        {
            yield return type;
        }
    }

    /// <summary>Polls until <paramref name="until" /> holds, so a test says what it is waiting for rather than how long.</summary>
    private static async Task<bool> WaitFor(Func<bool> until, TimeSpan timeout)
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

    private static async Task<DevToolsComponentNode?> WaitForTree(DevToolsFeed feed, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (feed.TreeSnapshot() is { } tree)
            {
                return tree;
            }

            await Task.Delay(25);
        }

        return feed.TreeSnapshot();
    }

    private static RaskTestHost Host() =>
        RaskTestHost.Create<DevToolsTestApp>(
            configureServices: s => RaskDevToolsLoader.Attach(s),
            configureMiddleware: app => app.Use((ctx, next) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
                return next(ctx);
            }),
            environment: "Development");

    /// <summary>The app's page, live on a socket: the session, its feed, the socket, and the page's click handler.</summary>
    private static async Task<(LiveSessionBase Session, DevToolsFeed Feed, System.Net.WebSockets.WebSocket Socket, string HandlerId)>
        LivePage(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/");
        var sessionId = SessionId().Match(html).Groups[1].Value;
        var handlerId = HandlerId().Match(html).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(handlerId), "the page has no click handler:" + Environment.NewLine + html);

        var session = host.Store.Get(sessionId);
        Assert.NotNull(session);
        var feed = host.Services.GetRequiredService<DevToolsFeeds>().For(session);

        var socket = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await socket.SendJsonAsync(new { type = "hello", session = sessionId });
        while (await socket.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300)) is not null)
        {
        }

        return (session, feed, socket, handlerId);
    }

    [GeneratedRegex("data-rask-root=\"([^\"]+)\"")]
    private static partial Regex SessionId();

    [GeneratedRegex("data-rask-on-click=\"([^\"]+)\"")]
    private static partial Regex HandlerId();
}
