using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class PayloadDedupTests
{
    [Fact]
    public async Task A_handler_that_does_not_change_visible_state_sends_no_frame()
    {
        using var host = RaskTestHost.Create<NoOpApp>();
        var initial = await host.Http.GetAsync("/start", TestContext.Current.CancellationToken);
        var initialHtml = await initial.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        // Trigger the no-op handler. Render output is identical to the recovery render, so the
        // server must suppress the frame.
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        var afterFirstClick = await ws.SettledAsync();

        Assert.Empty(afterFirstClick);

        // Second click — also a no-op, also suppressed.
        await ws.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        var afterSecondClick = await ws.SettledAsync();

        Assert.Empty(afterSecondClick);
    }
}
