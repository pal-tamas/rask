using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Server.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
///     A tab whose browser is starved reaches the host after its session's unconnected grace ran out. The page it was
///     served carries a sealed record of itself, bound to that session and its reader, so the tab is rebuilt where it
///     stands instead of reloading — and losing what the reader did meanwhile (#1258).
/// </summary>
public sealed class FirstPageResumeTests
{
    private const string UserHeader = "X-Test-User";

    private sealed class RouteApp(RouteState route) : Component
    {
        protected override string? HtmlLang => null;

        protected override Component? Render() => Markup.Div.Id("where")[route.Path];
    }

    [Fact]
    public async Task A_served_page_carries_a_record_an_attribute_holds_unescaped()
    {
        using var host = RaskTestHost.Create<RouteApp>();

        var html = await host.Http.GetStringAsync("/orders/7", TestContext.Current.CancellationToken);

        var record = Attribute(html, "data-rask-resume");
        Assert.NotNull(record);
        Assert.Matches("^[A-Za-z0-9_-]+$", record);
        TestContext.Current.TestOutputHelper?.WriteLine($"data-rask-resume: {record.Length} chars");
        Assert.InRange(record.Length, 1, 200);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_session_removed_before_its_first_connection_is_rebuilt_from_its_pages_record(LiveTransportKind kind)
    {
        using var host = RaskTestHost.Create<RouteApp>();
        var (sessionId, record) = await ServeAsync(host, "/orders/7");
        await host.Store.RemoveAsync(sessionId, SessionRemoval.Unconnected);

        await using var connection = await LiveTestConnection.OpenAsync(host, kind, sessionId, record);
        var frame = await connection.ReceiveUntilAsync(static f => Html(f) is not null, "the rebuilt page");

        Assert.Contains("/orders/7", Html(frame), StringComparison.Ordinal);
        Assert.Equal(1, host.Store.Count);
        Assert.Null(host.Store.Peek(sessionId));
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_click_the_reader_made_before_the_rebuild_runs_on_the_rebuilt_page(LiveTransportKind kind)
    {
        using var host = RaskTestHost.Create<CounterApp>();
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        await host.Store.RemoveAsync(sessionId, SessionRemoval.Unconnected);
        await using var connection = await LiveTestConnection.OpenAsync(host, kind, sessionId, Attribute(html, "data-rask-resume"));
        await connection.ReceiveUntilAsync(static f => Html(f) is not null, "the rebuilt page");

        await connection.SendJsonAsync(new { id = ClickId(html), type = "click", seq = 1, v = 0 });
        var counted = await connection.ReceiveUntilAsync(static f => Html(f)?.Contains("n=1", StringComparison.Ordinal) == true
                                                                   || f.Contains("\"n=1", StringComparison.Ordinal), "the click's render");

        Assert.NotNull(counted);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Every_click_after_a_rebuild_runs_as_on_any_page(LiveTransportKind kind)
    {
        using var host = RaskTestHost.Create<ClickCounterApp>(configureServices: static s => s.AddSingleton<Clicks>());
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        await host.Store.RemoveAsync(sessionId, SessionRemoval.Unconnected);
        await using var connection = await LiveTestConnection.OpenAsync(host, kind, sessionId, Attribute(html, "data-rask-resume"));
        await connection.SendJsonAsync(new { id = ClickId(html), type = "click", seq = 1, v = 0 });
        var version = 0;
        var page = html;

        for (var n = 1; n <= 3; n++)
        {
            if (n > 1)
            {
                await connection.SendJsonAsync(new { id = ClickId(page), type = "click", seq = n, v = version });
            }

            while (true)
            {
                var frame = await connection.ReceiveTextAsync();
                using var doc = JsonDocument.Parse(frame);
                if (doc.RootElement.TryGetProperty("v", out var v))
                {
                    version = v.GetInt32();
                }

                page = Html(frame) ?? page;
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ack")
                {
                    break;
                }
            }
        }

        Assert.Equal(3, host.Services.GetRequiredService<Clicks>().Count);
    }

    /// <summary>Every click any page counted, read after the page has gone.</summary>
    private sealed class Clicks
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add() => Interlocked.Increment(ref _count);
    }

    private sealed class ClickCounterApp(Clicks clicks) : Component
    {
        private int _n;

        protected override string? HtmlLang => null;

        protected override Component? Render() =>
        [
            Markup.P.Id("n")[$"n={_n}"],
            Markup.Button.Id("add").OnClick(() =>
            {
                _n++;
                clicks.Add();
            })["add"]
        ];
    }

    private sealed class CounterApp : Component
    {
        private int _n;

        protected override string? HtmlLang => null;

        protected override Component? Render() =>
        [
            Markup.P.Id("n")[$"n={_n}"],
            Markup.Button.Id("add").OnClick(() => _n++)["add"]
        ];
    }

    private static string ClickId(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "id=\"add\"[^>]*data-rask-on-click=\"([^\"]+)\"");
        Assert.True(match.Success, "the button's handler id");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task A_pages_record_opens_only_for_the_session_it_was_served_with()
    {
        using var host = RaskTestHost.Create<RouteApp>();
        var (_, record) = await ServeAsync(host, "/orders/7");
        var (otherId, _) = await ServeAsync(host, "/");
        await host.Store.RemoveAsync(otherId, SessionRemoval.Unconnected);

        await using var connection = await LiveTestConnection.OpenAsync(host, LiveTransportKind.WebSocket, otherId, record);
        var frame = await connection.ReceiveTextAsync();

        Assert.Contains("\"status\":\"unknown\"", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pages_record_is_never_opened_for_another_user()
    {
        using var host = RaskTestHost.Create<RouteApp>(configureMiddleware: StampUser);
        var (sessionId, record) = await ServeAsync(host, "/orders/7", user: "alice");
        await host.Store.RemoveAsync(sessionId, SessionRemoval.Unconnected);
        host.WebSockets.ConfigureRequest = static request => request.Headers[UserHeader] = "mallory";

        await using var connection = await LiveTestConnection.OpenAsync(host, LiveTransportKind.WebSocket, sessionId, record);
        var frame = await connection.ReceiveTextAsync();

        Assert.Contains("\"status\":\"unknown\"", frame, StringComparison.Ordinal);
        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task With_resume_off_a_page_carries_no_record()
    {
        using var host = RaskTestHost.Create<RouteApp>(configureServer: static o => o.SessionResume = false);

        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Null(Attribute(html, "data-rask-resume"));
    }

    private static async Task<(string SessionId, string Record)> ServeAsync(RaskTestHost host, string path, string? user = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null)
        {
            request.Headers.Add(UserHeader, user);
        }

        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (MarkupAssert.SessionId(html), Attribute(html, "data-rask-resume")!);
    }

    private static void StampUser(IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue(UserHeader, out var name) && !string.IsNullOrEmpty(name))
            {
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, name!)], "Test"));
            }

            await next(ctx);
        });

    private static string? Attribute(string html, string name)
    {
        var marker = " " + name + "=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        return html[start..html.IndexOf('"', start)];
    }

    private static string? Html(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.TryGetProperty("html", out var html) && html.ValueKind == JsonValueKind.String
            ? html.GetString()
            : null;
    }
}
