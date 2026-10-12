using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// What the browser is SENT when a handler outside a form — a list's delete — is refused by the store, or
// fails: a sentence in the toast, as diffs, and the page it was on still answering.
[Collection("HostEnvironment")]
public sealed partial class HandlerRefusalFramesTests
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
    public async Task A_handler_that_throws_anything_else_is_answered_in_production_with_a_toast_that_says_nothing_of_it()
    {
        await using var list = await Open("Production");

        var (frames, logged) = await Logged(() => list.Click("fail"));

        var told = Told(frames);
        Assert.Contains("That did not work. Please try again. The problem has been recorded.", told, StringComparison.Ordinal);
        Assert.Contains("data-variant=\"danger\"", told, StringComparison.Ordinal);
        Assert.Contains("attempts=1", told, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-error-boundary", told, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", told, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", told, StringComparison.Ordinal);
        Assert.DoesNotContain("devError", told, StringComparison.Ordinal);
        var line = Assert.Single(logged);
        Assert.Equal(RaskLogLevel.Error, line.Level);
        Assert.Equal("Rask.Errors", line.Category);
        Assert.EndsWith($"Reference {Reference().Match(told).Groups[1].Value}", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_click_after_a_failed_one_is_answered_on_the_same_session()
    {
        await using var list = await Open("Production");
        await list.Click("fail");

        var frames = await list.Click("fail");

        Assert.Contains("attempts=2", Told(frames), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_handler_in_development_keeps_the_page_and_names_the_exception_in_the_toast_and_beside_it()
    {
        await using var list = await Open("Development");

        var frames = await list.Click("fail");

        var told = Told(frames);
        Assert.Contains("\"devError\"", told, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"handler\"", told, StringComparison.Ordinal);
        Assert.Matches(
            "quote this reference: [A-Z0-9]{8} InvalidOperationException: Login failed for user 'sa' with password 'hunter2'\\.", told);
        Assert.Contains("attempts=1", told, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-error-boundary", told, StringComparison.Ordinal);
    }

    // What `act` was answered with, and the lines logged for the exception that carries the secret.
    private static async Task<(List<string> Frames, List<RaskDiagnosticEvent> Logged)> Logged(Func<Task<List<string>>> act)
    {
        var captured = new System.Collections.Concurrent.ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Enqueue;
        try
        {
            var frames = await act();
            return (frames, captured.Where(static line => line.Exception?.Message.Contains("hunter2", StringComparison.Ordinal) == true).ToList());
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    [GeneratedRegex("quote this reference: ([A-Z0-9]{8})", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Reference();

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
