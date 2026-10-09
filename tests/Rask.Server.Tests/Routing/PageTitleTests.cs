using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

// #1239 over real HTTP and a real socket: the document a GET answers with already has the page's title
// in the layout's crumb and in <title>, and a navigation carries both in the one frame it sends.
public class PageTitleTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("/titled/list", "Records")]
    [InlineData("/titled/edit/7", "Edit Record 7")]
    [InlineData("/titled/fetched/7", "Edit Record 7")]
    public async Task The_first_html_carries_the_title_in_the_crumb_and_in_the_document_title(string path, string title)
    {
        using var host = RaskTestHost.Create<PageTitleApp>();

        var html = await host.Http.GetStringAsync(path, TestContext.Current.CancellationToken);

        Assert.Contains($">{title} | App</title>", html, StringComparison.Ordinal);
        Assert.Contains($"<span id=\"crumb\">{title}</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_without_a_title_gets_no_crumb_and_the_layouts_own_document_title()
    {
        using var host = RaskTestHost.Create<PageTitleApp>();

        var html = await host.Http.GetStringAsync("/titled/plain", TestContext.Current.CancellationToken);

        Assert.Contains(">App</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"crumb\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_writes_the_title_above_its_router_serves_it_in_the_first_html()
    {
        using var host = RaskTestHost.Create<PageTitleShellApp>();

        var html = await host.Http.GetStringAsync("/bare/7", TestContext.Current.CancellationToken);

        Assert.Contains(">Edit Record 7 | Shell</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Shell</title>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_navigation_sends_one_frame_with_the_new_crumb_and_the_new_head()
    {
        using var host = RaskTestHost.Create<PageTitleApp>(diffMode: LiveDiffMode.DisabledFull);
        using var ws = await Connect(host, "/titled/list");

        await ws.SendJsonAsync(new { type = "navigate", path = "/titled/edit/7", query = "" }, TestContext.Current.CancellationToken);
        var first = await ws.TryReceiveTextAsync(Wait);
        var second = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));

        Assert.NotNull(first);
        using var doc = JsonDocument.Parse(first);
        var html = doc.RootElement.GetProperty("html").GetString();
        Assert.Contains(">Edit Record 7 | App</title>", html, StringComparison.Ordinal);
        Assert.Contains("<span id=\"crumb\">Edit Record 7</span>", html, StringComparison.Ordinal);
        Assert.Null(second);
    }

    [Fact]
    public async Task A_navigation_under_an_app_that_writes_the_title_itself_carries_the_new_head_in_its_diff()
    {
        using var host = RaskTestHost.Create<PageTitleShellApp>(diffMode: LiveDiffMode.Forced);
        using var ws = await Connect(host, "/bare/7");

        await ws.SendJsonAsync(new { type = "navigate", path = "/bare/8", query = "" }, TestContext.Current.CancellationToken);
        var frame = await ws.TryReceiveTextAsync(Wait);

        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame);
        Assert.Equal("diff", doc.RootElement.GetProperty("kind").GetString());
        Assert.Contains("Edit Record 8 | Shell", doc.RootElement.GetProperty("head").GetString(), StringComparison.Ordinal);
        Assert.Contains("Edit Record 8", doc.RootElement.GetProperty("ops").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_title_slower_than_the_response_budget_is_left_out_and_arrives_over_the_socket()
    {
        PageTitleSlowPage.Loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = RaskTestHost.Create<PageTitleApp>(
            configureServer: o => o.QuiescenceTimeout = TimeSpan.FromMilliseconds(150),
            diffMode: LiveDiffMode.DisabledFull);
        var response = await host.Http.GetAsync("/titled/slow", TestContext.Current.CancellationToken);
        var served = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var ws = await Attach(host, served);

        PageTitleSlowPage.Loaded.SetResult();
        var frame = await ws.TryReceiveTextAsync(Wait);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(">App</title>", served, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"crumb\"", served, StringComparison.Ordinal);
        Assert.NotNull(frame);
        using var doc = JsonDocument.Parse(frame);
        var html = doc.RootElement.GetProperty("html").GetString();
        Assert.Contains(">Slow record | App</title>", html, StringComparison.Ordinal);
        Assert.Contains("<span id=\"crumb\">Slow record</span>", html, StringComparison.Ordinal);
    }

    private static async Task<WebSocket> Connect(RaskTestHost host, string path) =>
        await Attach(host, await host.Http.GetStringAsync(path, TestContext.Current.CancellationToken));

    private static async Task<WebSocket> Attach(RaskTestHost host, string servedHtml)
    {
        var sessionId = MarkupAssert.SessionId(servedHtml);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId, Wait);
        return ws;
    }
}
