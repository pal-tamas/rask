using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     A panel page renders only in the panel's own document. A live app session can navigate itself onto the panel's path
///     over its socket, which no admission check sees, so the panel's layout refuses to render its tabs anywhere its shell
///     is not the root.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed partial class DevToolsPanelIsolationTests
{
    private const string Refusal = "opens only in its own frame";

    [Fact]
    public async Task An_app_session_that_navigates_to_its_own_panel_url_renders_no_tab()
    {
        using var host = Host();
        // No app route matches "/", so the page answers 404 — but its root still renders live, with its handler, its session
        // and its devtools tag, which is all this test needs from it.
        var html = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();
        var sessionId = SessionId().Match(html).Groups[1].Value;
        var panel = WebUtility.HtmlDecode(DataPanel().Match(html).Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(sessionId), "the page carries no session id:" + Environment.NewLine + html);
        Assert.False(string.IsNullOrEmpty(panel), "the page names no devtools panel:" + Environment.NewLine + html);

        var session = host.Store.Get(sessionId);
        Assert.NotNull(session);
        host.Services.GetRequiredService<DevToolsFeeds>().For(session)
            .RecordWire(DevToolsWireDirection.Out, "wire-isolation-event", 11, Stopwatch.GetTimestamp());

        using var socket = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await socket.SendJsonAsync(new { type = "hello", session = sessionId });
        while (await socket.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300)) is not null)
        {
        }

        // The panel URL the app's own page names — its own session and a valid token — so the only thing wrong is the
        // document it would render in.
        var queryAt = panel.IndexOf('?', StringComparison.Ordinal);
        await socket.SendJsonAsync(new { type = "navigate", path = panel[..queryAt], query = panel[queryAt..] });

        var frames = new List<string>();
        while (await socket.TryReceiveTextAsync(TimeSpan.FromSeconds(2)) is { } frame)
        {
            frames.Add(frame);
            if (frame.Contains(Refusal, StringComparison.Ordinal))
            {
                break;
            }
        }

        var all = string.Join(Environment.NewLine, frames);
        Assert.Contains(Refusal, all, StringComparison.Ordinal);
        Assert.DoesNotContain("wire-isolation-event", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Frames sent", all, StringComparison.Ordinal);
    }

    private static RaskTestHost Host() =>
        RaskTestHost.Create<DevToolsRoutedTestApp>(
            configureServices: s => RaskDevToolsLoader.Attach(s),
            configureMiddleware: app => app.Use((ctx, next) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
                return next(ctx);
            }),
            environment: "Development");

    [GeneratedRegex("data-panel=\"([^\"]+)\"")]
    private static partial Regex DataPanel();

    [GeneratedRegex("data-rask-root=\"([^\"]+)\"")]
    private static partial Regex SessionId();
}
