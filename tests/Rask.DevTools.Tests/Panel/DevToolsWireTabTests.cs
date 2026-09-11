using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Server;
using Rask.Server.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The Wire tab through the real panel page: what it lists, that it follows the inspected page live, and that the
///     panel finds a session only with that session's token and owner — on every render, not just at admission.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed partial class DevToolsWireTabTests
{
    [Fact]
    public async Task The_panel_lists_the_inspected_pages_traffic_newest_first()
    {
        using var host = Host();
        var (session, panel) = await InspectedPage(host);
        var feed = host.Services.GetRequiredService<DevToolsFeeds>().For(session);
        var at = Stopwatch.GetTimestamp();
        feed.RecordWire(DevToolsWireDirection.Out, "wire-test-event", 42, at);
        feed.RecordDiff(opCount: 3, usedDiff: true);
        feed.RecordWire(DevToolsWireDirection.In, "frame", 2048, at + Stopwatch.Frequency / 100);

        var html = await host.Http.GetStringAsync(panel);

        Assert.Contains("Frames sent", html, StringComparison.Ordinal);
        Assert.Contains("2 frames, newest first.", html, StringComparison.Ordinal);
        Assert.Contains("2.0 KB", html, StringComparison.Ordinal);
        Assert.Contains(">10 ms<", html, StringComparison.Ordinal);
        var frame = html.IndexOf("3 ops", StringComparison.Ordinal);
        var sent = html.IndexOf("wire-test-event", StringComparison.Ordinal);
        Assert.True(frame >= 0 && sent >= 0, "a row is missing:" + Environment.NewLine + html);
        Assert.True(frame < sent, "the newest frame is not listed first");
    }

    [Fact]
    public async Task A_panel_opened_before_any_traffic_says_so()
    {
        using var host = Host();
        var (_, panel) = await InspectedPage(host);

        var html = await host.Http.GetStringAsync(panel);

        Assert.Contains("No traffic yet.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_open_panel_shows_traffic_recorded_after_it_connected()
    {
        using var host = Host();
        var (session, panel) = await InspectedPage(host);
        var panelHtml = await host.Http.GetStringAsync(panel);
        using var socket = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await socket.SendJsonAsync(new { type = "hello", session = SessionId().Match(panelHtml).Groups[1].Value });
        while (await socket.TryReceiveTextAsync(TimeSpan.FromMilliseconds(300)) is not null)
        {
        }

        host.Services.GetRequiredService<DevToolsFeeds>().For(session)
            .RecordWire(DevToolsWireDirection.Out, "wire-live-event", 7, Stopwatch.GetTimestamp());

        string? frame;
        do
        {
            frame = await socket.TryReceiveTextAsync(TimeSpan.FromSeconds(5));
        }
        while (frame is not null && !frame.Contains("wire-live-event", StringComparison.Ordinal));

        Assert.NotNull(frame);
    }

    [Fact]
    public async Task The_inspection_opens_a_session_only_with_its_token()
    {
        using var host = Host();
        var (session, panel) = await InspectedPage(host);
        var (id, token) = Credentials(panel);
        using var scope = host.Services.CreateScope();
        var inspection = scope.ServiceProvider.GetRequiredService<IDevToolsInspection>();

        Assert.Same(host.Services.GetRequiredService<DevToolsFeeds>().For(session), inspection.Open(id, token));
        Assert.Null(inspection.Open(id, "AAAA"));
        Assert.Null(inspection.Open(id, null));
        Assert.Null(inspection.Open("not-a-session", token));
    }

    [Fact]
    public async Task The_inspection_opens_a_signed_in_session_only_for_its_owner()
    {
        using var host = Host();
        var (session, panel) = await InspectedPage(host);
        var (id, token) = Credentials(panel);
        session.Services.GetRequiredService<SessionUserProvider>().Set(User("alice"));

        Assert.NotNull(InspectionAs(host, User("alice")).Open(id, token));
        Assert.Null(InspectionAs(host, User("bob")).Open(id, token));
        Assert.Null(InspectionAs(host, new ClaimsPrincipal(new ClaimsIdentity())).Open(id, token));
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

    /// <summary>Renders the app's page, and returns its live session and the panel URL its devtools tag names.</summary>
    private static async Task<(LiveSession Session, string Panel)> InspectedPage(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/");
        var panel = DataPanel().Match(html);
        Assert.True(panel.Success, "the page names no devtools panel:" + Environment.NewLine + html);

        var session = host.Store.Get(SessionId().Match(html).Groups[1].Value);
        Assert.NotNull(session);
        return (session, WebUtility.HtmlDecode(panel.Groups[1].Value));
    }

    private static (string Id, string Token) Credentials(string panel)
    {
        var query = QueryHelpers.ParseQuery(new Uri(new Uri("http://localhost"), panel).Query);
        return (query["inspect"].ToString(), query["t"].ToString());
    }

    /// <summary>A panel session's inspection, as seen by <paramref name="viewer" />.</summary>
    private static IDevToolsInspection InspectionAs(RaskTestHost host, ClaimsPrincipal viewer)
    {
        var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SessionUserProvider>().Set(viewer);
        return scope.ServiceProvider.GetRequiredService<IDevToolsInspection>();
    }

    private static ClaimsPrincipal User(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test"));

    [GeneratedRegex("data-panel=\"([^\"]+)\"")]
    private static partial Regex DataPanel();

    [GeneratedRegex("data-rask-root=\"([^\"]+)\"")]
    private static partial Regex SessionId();
}
