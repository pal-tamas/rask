using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A save refused with a message under its fields, then the radio that shows and hides the fields beneath it: the
// reader is answered with diffs, and the page never follows them, since it lands on whatever was typed since.
// The render the form's timer asks for a moment after a store has answered drew nothing new, but wrote the
// invalid field's two ARIA attributes in the other order. The differ pairs attributes by name, so the tree was
// unchanged, and the markup that was not the one last sent used to be answered with the whole page.
[Collection("HostEnvironment")]
public sealed class KindChosenAfterRefusedSaveFramesTests
{
    [Theory]
    [InlineData("Quarterly")]
    [InlineData("Periodic")]
    public async Task The_first_kind_chosen_after_a_refused_save_is_answered_with_diffs_and_no_page(string kind)
    {
        using var host = RaskTestHost.Create<KindFormApp>(environment: "Development");
        var (ws, html) = await OpenAsync(host);
        using var _ = ws;
        await ws.SendJsonAsync(new { id = MarkupAssert.RequireAttr(html, "data-rask-on-submit"), type = "submit", form = new { Name = "" } }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();
        var choose = Regex.Match(html, "id=\"f-kind-" + kind.ToLowerInvariant() + "\"[^>]*data-rask-on-change=\"([^\"]+)\"").Groups[1].Value;
        var captured = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Enqueue;

        List<string> frames;
        try
        {
            await ws.SendJsonAsync(new { id = choose, value = kind }, ct: TestContext.Current.CancellationToken);
            frames = await ws.SettledAsync();
            await host.Store.Snapshot().Single().RequestRender();
            frames.AddRange(await ws.SettledAsync());
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        var replies = frames.Where(static frame => !frame.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal)).ToList();
        var said = captured.Where(static e => string.Equals(e.Category, "Rask.Live", StringComparison.Ordinal)).Select(static e => e.Message);
        Assert.NotEmpty(replies);
        Assert.All(replies, static reply => Assert.StartsWith("{\"kind\":\"diff\"", reply, StringComparison.Ordinal));
        Assert.DoesNotContain(said, static message => message.Contains("whole page", StringComparison.Ordinal));
    }

    private static async Task<(WebSocket Socket, string Html)> OpenAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId, TimeSpan.FromSeconds(5));

        return (ws, html);
    }
}
