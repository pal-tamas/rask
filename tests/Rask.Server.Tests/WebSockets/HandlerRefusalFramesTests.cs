using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// What the browser is SENT when a handler outside a form — a list's delete — is refused by the store: its
// sentence in the toast, as diffs, and the page it was on still answering.
[Collection("HostEnvironment")]
public sealed class HandlerRefusalFramesTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task A_refused_delete_sends_the_stores_sentence_in_the_toast_as_a_diff_and_nothing_more(string environment)
    {
        await using var list = await Open(environment);

        var frames = await list.Click("delete");

        var told = Told(frames);
        Assert.Contains(RefusedDeleteApp.Sentence, told, StringComparison.Ordinal);
        Assert.Contains("data-variant=\"danger\"", told, StringComparison.Ordinal);
        Assert.Contains("attempts=1", told, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", told, StringComparison.Ordinal);
        Assert.DoesNotContain("FK_Waybill_Trailer", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_click_after_a_refused_one_is_answered_on_the_same_session()
    {
        await using var list = await Open("Production");
        await list.Click("delete");

        var frames = await list.Click("delete");

        Assert.Contains("attempts=2", Told(frames), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_handler_that_throws_anything_else_is_answered_with_the_error_page_and_no_toast_as_before()
    {
        await using var list = await Open("Production");

        var frames = await list.Click("fail");

        var told = string.Join('\n', frames.Select(Decoded));
        Assert.Contains("rask-error-boundary", told, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", told, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-toast-dialog", told, StringComparison.Ordinal);
    }

    // The markup of a frame as the browser reads it: out of its JSON string, and out of its character references.
    private static string Decoded(string frame) => System.Net.WebUtility.HtmlDecode(Regex.Unescape(frame));

    // Everything the click was answered with, read as one: each frame a diff, never the whole page.
    private static string Told(List<string> frames)
    {
        Assert.NotEmpty(frames);
        Assert.All(frames, static frame => Assert.StartsWith("{\"kind\":\"diff\"", frame, StringComparison.Ordinal));
        return string.Join('\n', frames.Select(Decoded));
    }

    private static async Task<OpenList> Open(string environment)
    {
        var host = RaskTestHost.Create<RefusedDeleteApp>(environment: environment);
        var html = await host.Http.GetStringAsync("/");
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);
        return new OpenList(host, ws, html);
    }

    private sealed partial class OpenList(RaskTestHost host, WebSocket ws, string html) : IAsyncDisposable
    {
        public async Task<List<string>> Click(string buttonId)
        {
            var handler = HandlerOf(buttonId).Match(html).Groups[1].Value;
            await ws.SendJsonAsync(new { id = handler, type = "click" });

            return (await ws.SettledAsync()).Where(static f => !f.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal)).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // The server may already have closed it.
            }

            ws.Dispose();
            host.Dispose();
        }

        private static Regex HandlerOf(string buttonId) =>
            new($"<button id=\"{buttonId}\"[^>]*data-rask-on-click=\"([^\"]+)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
