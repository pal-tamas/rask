using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class PayloadDedupTests
{
    [Fact]
    public async Task HandlerThatDoesNotChangeVisibleState_SuppressesWsFrame()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var handlerId = ExtractFirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        // Drain the recovery render that fires on hello.
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Trigger the no-op handler. Render output is identical to the recovery render, so the
        // server must suppress the frame.
        await ws.SendJsonAsync(new { id = handlerId });
        var afterFirstClick = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(afterFirstClick);

        // Second click — also a no-op, also suppressed.
        await ws.SendJsonAsync(new { id = handlerId });
        var afterSecondClick = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(afterSecondClick);
    }

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;

    private static string ExtractFirstHandlerId(string html)
    {
        var match = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"");
        Assert.True(match.Success, $"no handler attribute found in html: {html}");
        return match.Groups[1].Value;
    }
}
