using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A handler that awaits is repainted before each of its continuations. When that repaint threw — here the
// frame is lost on the way out — the continuation was never run: the handler's task never ended, it held the
// session's lock, and the session answered nothing ever again. The code after the await runs now, the fault
// reaches the boundary a handler's fault reaches, and the next event is answered.
public class MidAwaitRenderFaultTests
{
    [Fact]
    public async Task A_handler_whose_repaint_between_two_awaits_fails_still_finishes_and_the_next_click_is_answered()
    {
        TwoStepSaveApp.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TwoStepSaveApp.Finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TwoStepSaveApp.Outside = 0;
        var socket = new LosingWebSocket();
        using var host = RaskTestHost.Create<TwoStepSaveApp>(
            configureMiddleware: app => app.Use(socket.Wrap), diffMode: LiveDiffMode.DisabledFull);
        var html = await (await host.Http.GetAsync("/", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        await ws.SendJsonAsync(new { id = HandlerIdFor(html, "save"), seq = 1 }, ct: TestContext.Current.CancellationToken);
        await ws.ReceiveUntilAsync(frame => HtmlOf(frame).Contains("state=saving"), "the repaint at the handler's await");

        TwoStepSaveApp.Outside = 1;
        socket.LoseNext();
        TwoStepSaveApp.Gate.SetResult();
        await TwoStepSaveApp.Finished.Task.WaitAsync(LiveFrames.HangCeiling, TestContext.Current.CancellationToken);
        var seen = new List<string>();
        await ws.ReceiveUntilAsync(frame => Keep(seen, frame) && AckOf(frame) == 1, "the ack of the save");
        await ws.SendJsonAsync(new { id = HandlerIdFor(html, "bump"), seq = 2 }, ct: TestContext.Current.CancellationToken);
        await ws.ReceiveUntilAsync(frame => AckOf(frame) == 2, "the ack of the click after the save");

        Assert.Contains(seen, frame => HtmlOf(frame).Contains("state=saved"));
        Assert.Contains(seen, frame => HtmlOf(frame).Contains("Application error"));
    }

    private static bool Keep(List<string> seen, string frame)
    {
        seen.Add(frame);
        return true;
    }

    private static string HandlerIdFor(string html, string buttonText)
    {
        var match = Regex.Match(
            html,
            "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>[^<]*" + Regex.Escape(buttonText));
        Assert.True(match.Success, $"button '{buttonText}' not found");
        return match.Groups[1].Value;
    }

    private static string HtmlOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("html", out var html) ? html.GetString() ?? string.Empty : string.Empty;
    }

    private static long AckOf(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ack"
            ? doc.RootElement.GetProperty("seq").GetInt64()
            : -1;
    }
}
