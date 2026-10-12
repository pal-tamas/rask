using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// What the browser is SENT when a form's save throws something that names no field: the toast, as diffs. How
// many frames carry it is not pinned here, since a field may answer its own change beside it.
[Collection("HostEnvironment")]
public sealed class SaveFaultFramesTests(ITestOutputHelper output)
{
    private const string SaveFailed = "The save failed. Please try again.";

    [Theory]
    [InlineData("tenant")]
    [InlineData("other")]
    public async Task A_save_that_throws_in_production_sends_the_toast_as_a_diff_with_no_word_of_the_exception(string name)
    {
        await using var form = await Open("Production");

        var frames = await form.Save(name);

        var toast = Told(frames);
        Assert.Contains(SaveFailed, toast, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", toast, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", toast, StringComparison.Ordinal);
        Assert.DoesNotContain("4114", toast, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_save_that_throws_in_development_says_which_exception_in_the_toast_as_well()
    {
        await using var form = await Open("Development");

        var frames = await form.Save("tenant");

        var toast = Told(frames);
        Assert.Contains($"{SaveFailed} MissingTenantException: No tenant is selected for partner 4114", toast, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task A_validation_sentence_thrown_by_the_save_is_the_toast_as_it_was_written(string environment)
    {
        await using var form = await Open(environment);

        var frames = await form.Save("rule");

        var toast = Told(frames);
        Assert.Contains(SaveFaultApp.Sentence, toast, StringComparison.Ordinal);
        Assert.DoesNotContain(SaveFailed, toast, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidationException", toast, StringComparison.Ordinal);
    }

    // The markup of a frame as the browser reads it: out of its JSON string, and out of its character references.
    private static string Decoded(string frame) => System.Net.WebUtility.HtmlDecode(Regex.Unescape(frame));

    // Everything the save was answered with, read as one: each frame a diff, never the whole page.
    private static string Told(List<string> frames)
    {
        Assert.NotEmpty(frames);
        Assert.All(frames, static frame => Assert.StartsWith("{\"kind\":\"diff\"", frame, StringComparison.Ordinal));
        return string.Join('\n', frames.Select(Decoded));
    }

    private async Task<OpenForm> Open(string environment)
    {
        var host = RaskTestHost.Create<SaveFaultApp>(environment: environment);
        var html = await host.Http.GetStringAsync("/");
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        await ws.AttachedAsync(host, sessionId);
        return new OpenForm(host, ws, html, output);
    }

    private sealed class OpenForm(RaskTestHost host, WebSocket ws, string html, ITestOutputHelper output) : IAsyncDisposable
    {
        // A name typed, then "Mentés" pressed with the field still focused: the field waits, so the browser sends
        // one frame, the value first and the submit behind it.
        public async Task<List<string>> Save(string name)
        {
            var submit = new { id = MarkupAssert.RequireAttr(html, "data-rask-on-submit"), type = "submit", form = new { Name = name } };
            await ws.SendTypedThenAsync(MarkupAssert.RequireAttr(html, "data-rask-on-change"), name, submit);

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
