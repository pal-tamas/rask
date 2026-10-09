using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// #1238: a crumb in the layout whose whole render is `Text[heading.Text]` stayed `<div></div>` for ever, while the
// same leaf written `Span[heading.Text]` was patched. Text dropped the words its indexer handed it, so the leaf
// re-rendered into nothing every time. The page beside it is what names the heading, as it mounts — after the
// layout's crumb has already been walked — which is the second half pinned here: the first response has to carry
// what that mount asked for, not arrive one render behind it.
public class TextRootRerenderTests
{
    private const string TextStep = "<nav id=\"text\"><div class=\"item\"><div class=\"step\">";
    private const string ValueStep = "<nav id=\"value\"><div class=\"item\"><div class=\"step\">";

    // document > html > body > nav > div.item > div.step > the text node itself.
    private static readonly int[] TextCrumbPath = [1, 1, 0, 0, 0, 0];
    private static readonly int[] ValueCrumbPath = [1, 1, 1, 0, 0, 0];

    [Fact]
    public async Task The_first_response_carries_the_words_a_page_set_while_it_mounted()
    {
        using var host = CrumbHost();

        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Contains(TextStep + "Orders</div>", html);
        Assert.Contains(ValueStep + "Orders</div>", html);
        Assert.Contains("<span>Orders</span>", html);
    }

    [Fact]
    public async Task A_bare_text_root_is_updated_over_the_socket_when_its_words_change()
    {
        using var host = CrumbHost();
        var (ws, html) = await Connect(host);
        using var socket = ws;

        var frame = await Click(ws, html, "rename");

        Assert.Equal("Invoices", OpAt(frame, TextCrumbPath).Value);
        Assert.Equal("Invoices", OpAt(frame, ValueCrumbPath).Value);
        Assert.Equal(3, OpAt(frame, TextCrumbPath).Kind);
    }

    [Fact]
    public async Task A_bare_text_root_is_removed_and_inserted_again_over_the_socket()
    {
        using var host = CrumbHost();
        var (ws, html) = await Connect(host);
        using var socket = ws;

        var cleared = await Click(ws, html, "clear");
        var renamed = await Click(ws, html, "rename");

        Assert.Equal(5, OpAt(cleared, TextCrumbPath).Kind);
        Assert.Equal(4, OpAt(renamed, TextCrumbPath).Kind);
        Assert.Equal("Invoices", OpAt(renamed, TextCrumbPath).Value);
    }

    [Fact]
    public async Task A_response_that_does_not_wait_still_gets_the_words_once_the_socket_is_up()
    {
        // No budget, no waves: the GET serves its first render, and the request the page's mount made is
        // owed to the browser as the catch-up frame.
        using var host = CrumbHost(o => o.QuiescenceTimeout = TimeSpan.Zero);
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);

        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(html) }, ct: TestContext.Current.CancellationToken);
        var frame = await ws.ReceiveTextAsync();

        Assert.Contains(TextStep + "</div>", html);
        Assert.Equal(4, OpAt(frame, TextCrumbPath).Kind);
        Assert.Equal("Orders", OpAt(frame, TextCrumbPath).Value);
    }

    [Fact]
    public async Task The_first_response_carries_what_a_page_told_its_layout_from_its_mount_and_update_hooks()
    {
        using var host = RaskTestHost.Create<TitledLayoutApp>(s => s.AddScoped<LayoutHandle>());

        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);

        Assert.Contains("mounted=orders", html);
        Assert.Contains("updated=orders", html);
    }

    [Fact]
    public async Task A_layout_re_renders_when_a_page_below_it_asks_it_to()
    {
        using var host = RaskTestHost.Create<TitledLayoutApp>(s => s.AddScoped<LayoutHandle>());
        var (ws, html) = await Connect(host);
        using var socket = ws;

        var frame = await Click(ws, html, "rename");

        Assert.Contains("mounted=invoices", frame);
    }

    private static RaskTestHost CrumbHost(Action<RaskServerOptions>? configureServer = null) =>
        RaskTestHost.Create<TextRootCrumbApp>(s => s.AddScoped<CrumbTitle>(), configureServer: configureServer);

    // The page as the browser holds it, with the hello-time catch-up (if the session owed one) already read.
    private static async Task<(WebSocket Ws, string Html)> Connect(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();
        return (ws, html);
    }

    private static async Task<string> Click(WebSocket ws, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        await ws.SendJsonAsync(new { id = handler }, ct: TestContext.Current.CancellationToken);
        return await ws.ReceiveTextAsync();
    }

    private static (int Kind, string? Value) OpAt(string frame, int[] path)
    {
        using var doc = JsonDocument.Parse(frame);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            if (op[1].EnumerateArray().Select(slot => slot.GetInt32()).SequenceEqual(path))
            {
                return (op[0].GetInt32(), op.GetArrayLength() > 2 && op[2].ValueKind == JsonValueKind.String ? op[2].GetString() : null);
            }
        }

        Assert.Fail($"no op addresses [{string.Join(',', path)}] in {frame}");
        return default;
    }
}
