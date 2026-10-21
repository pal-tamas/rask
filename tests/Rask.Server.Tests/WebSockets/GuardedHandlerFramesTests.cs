using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// What the browser is SENT when it posts the id of a handler whose method carries [Authorize] the session's person
// does not meet: the forbidden sentence in a toast, and the page as it was. Whatever the frame says, the server
// asks the session's principal before the method runs.
public sealed partial class GuardedHandlerFramesTests
{
    [Theory]
    [InlineData("by-method-group")]
    [InlineData("by-lambda")]
    public async Task A_forged_handler_post_is_refused_with_the_sentence_and_changes_nothing(string button)
    {
        await using var page = await Open();

        var frames = await page.Post(button);

        var told = string.Join('\n', frames.Select(static frame => System.Net.WebUtility.HtmlDecode(Regex.Unescape(frame))));
        Assert.Contains("You do not have permission to do that.", told, StringComparison.Ordinal);
        Assert.Contains("data-variant=\"danger\"", told, StringComparison.Ordinal);
        Assert.DoesNotContain("removed=1", told, StringComparison.Ordinal);
    }

    private static async Task<OpenPage> Open()
    {
        var host = RaskTestHost.Create<GuardedRemoveApp>();
        var html = await host.Http.GetStringAsync("/");
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);
        return new OpenPage(host, ws, html);
    }

    private sealed class OpenPage(RaskTestHost host, WebSocket ws, string html) : IAsyncDisposable
    {
        // The click frame a browser would send, written by hand: the id read off the page, nothing else.
        public async Task<List<string>> Post(string buttonId)
        {
            var handler = new Regex($"<button id=\"{buttonId}\"[^>]*data-rask-on-click=\"([^\"]+)\"", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)).Match(html).Groups[1].Value;
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
    }
}
