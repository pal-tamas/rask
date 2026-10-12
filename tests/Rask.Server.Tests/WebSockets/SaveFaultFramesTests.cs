using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// What the browser is SENT when a form's save throws something that names no field, and when a save is refused
// for a name the field was already told is taken. Pinned as it is today; "today" tests flip with the fix.
// The second flipped with the handler budget: a handler's passing states are no longer sent.
[Collection("HostEnvironment")]
public sealed class SaveFaultFramesTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Production", "tenant")]
    [InlineData("Production", "rule")]
    [InlineData("Production", "other")]
    [InlineData("Development", "tenant")]
    [InlineData("Development", "rule")]
    public async Task Today_a_save_that_throws_sends_the_browser_nothing_but_its_acknowledgement(string environment, string name)
    {
        await using var form = await Open(environment);

        var frames = await form.Save(name);

        // Wanted: a frame that tells the reader the save failed. Today the socket is silent, in Development too.
        Assert.Empty(frames);
    }

    [Fact]
    public async Task A_save_refused_for_a_name_the_field_already_said_is_taken_sends_the_message_once_when_the_field_is_left()
    {
        // Longer than this may last: the save's check is answered within the budget, never because it ran out.
        await using var form = await Open("Production", static o => o.HandlerQuiescenceTimeout = TimeSpan.FromMinutes(5));

        var frames = await form.Save("p");

        // The save's own check takes the message off and puts it back. Neither is sent: a handler's passing
        // states are withheld while it runs, and it ends on the page the reader already has.
        Assert.Contains(SaveFaultApp.Taken, Decoded(Assert.Single(frames)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_handler_budget_that_save_clears_the_message_and_sets_it_again_as_it_always_did()
    {
        await using var form = await Open("Production", static o => o.HandlerQuiescenceTimeout = TimeSpan.Zero);

        var frames = await form.Save("p");

        Assert.Equal(3, frames.Count);
        Assert.Contains(SaveFaultApp.Taken, Decoded(frames[0]), StringComparison.Ordinal);
        Assert.DoesNotContain(SaveFaultApp.Taken, Decoded(frames[1]), StringComparison.Ordinal);
        Assert.Contains(SaveFaultApp.Taken, Decoded(frames[2]), StringComparison.Ordinal);
    }

    // The markup of a frame as the browser reads it: out of its JSON string, and out of its character references.
    private static string Decoded(string frame) => System.Net.WebUtility.HtmlDecode(Regex.Unescape(frame));

    private async Task<OpenForm> Open(string environment, Action<RaskServerOptions>? configure = null)
    {
        var host = RaskTestHost.Create<SaveFaultApp>(environment: environment, configureServer: configure);
        var html = await host.Http.GetStringAsync("/");
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);
        return new OpenForm(host, ws, html, output);
    }

    private sealed class OpenForm(RaskTestHost host, WebSocket ws, string html, ITestOutputHelper output) : IAsyncDisposable
    {
        // A name typed, then "Mentés" pressed with the field still focused: the browser sends the field's change
        // and the submit one after the other. What the keystrokes were answered with is read past.
        public async Task<List<string>> Save(string name)
        {
            await ws.SendJsonAsync(new { id = MarkupAssert.Attr(html, "data-rask-on-input"), type = "input", value = name, seq = 1 });
            await ws.SettledAsync();
            await ws.SendJsonAsync(new { id = MarkupAssert.Attr(html, "data-rask-on-change"), type = "change", value = name, seq = 2 });
            await ws.SendJsonAsync(new { id = MarkupAssert.Attr(html, "data-rask-on-submit"), type = "submit", form = new { Name = name }, seq = 3 });

            var frames = (await ws.SettledAsync()).Where(static f => !f.StartsWith("{\"type\":\"ack\"", StringComparison.Ordinal)).ToList();
            foreach (var frame in frames)
            {
                output.WriteLine($"  {Encoding.UTF8.GetByteCount(frame)} B {frame[..Math.Min(frame.Length, 120)]}");
            }

            return frames;
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
