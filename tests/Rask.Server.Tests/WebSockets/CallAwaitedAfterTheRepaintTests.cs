using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     A handler that awaits a call into the browser after its page has already gone out once: the call is sent
///     while the handler waits on it, whichever context the handler's code runs on by then.
/// </summary>
/// <remarks>
///     The dispatch renders when the handler ends, and the handler ends when the browser has answered, so the
///     call rides a repaint made meanwhile. Only a continuation that came back through the handler's context got
///     one (<c>HandlerSyncContext</c>); the kit's handlers await with <c>ConfigureAwait(false)</c>, and a pick
///     that wrote its text and then asked for focus held its session with the second call still in the queue —
///     <c>SearchDebounceTests</c>' autocomplete on a loaded runner, where the browser's first answer came back
///     after the repaint had returned.
/// </remarks>
public class CallAwaitedAfterTheRepaintTests
{
    [Fact]
    public async Task Two_calls_a_handler_awaits_off_its_context_are_each_sent_while_it_waits()
    {
        var slow = new TwoCallsAwaitedApp.Slow();
        using var host = RaskTestHost.Create<TwoCallsAwaitedApp>(services => services.AddSingleton(slow));
        var page = await (await host.Http.GetAsync("/", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(page) }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();

        await ws.SendJsonAsync(new { id = MarkupAssert.RequireAttr(page, "data-rask-on-click") }, ct: TestContext.Current.CancellationToken);
        await ws.ReceiveUntilAsync(frame => frame.Contains("waiting", StringComparison.Ordinal), "the page the handler's budget shows");
        slow.Done.SetResult();
        var write = await ws.ReceiveUntilAsync(frame => Asks(frame, "test.write"), "the frame that carries the first call");
        await ws.SendJsonAsync(new { type = "jsResult", id = CallId(write), success = true }, ct: TestContext.Current.CancellationToken);
        var focus = await ws.ReceiveUntilAsync(frame => Asks(frame, "test.focus"), "the frame that carries the second call");
        await ws.SendJsonAsync(new { type = "jsResult", id = CallId(focus), success = true }, ct: TestContext.Current.CancellationToken);
        var done = await ws.ReceiveUntilAsync(frame => frame.Contains("focused", StringComparison.Ordinal), "the page of the handler that ended");

        Assert.Contains("written", focus, StringComparison.Ordinal);
        Assert.DoesNotContain("jsInvokes", done, StringComparison.Ordinal);
    }

    private static bool Asks(string frame, string identifier) => frame.Contains($"\"{identifier}\"", StringComparison.Ordinal);

    private static long CallId(string frame)
    {
        using var read = JsonDocument.Parse(frame);

        return read.RootElement.GetProperty("jsInvokes")[0].GetProperty("id").GetInt64();
    }
}
