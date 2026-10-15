using System.Globalization;
using System.Net.WebSockets;
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
///     <para>
///         The browser here is the test, so an answer is sent when the test says: after the frame that asked
///         for it has been read, which is after the repaint that carried it.
///     </para>
/// </remarks>
public class CallAwaitedAfterTheRepaintTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Calls_a_handler_awaits_in_a_row_off_its_context_are_each_sent_while_it_waits(int calls)
    {
        var plan = new CallsAwaitedApp.Plan(calls, KeepsContext: false);
        using var host = RaskTestHost.Create<CallsAwaitedApp>(services => services.AddSingleton(plan));
        using var ws = await PressedAsync(host, plan);

        var carried = await AnswerEachAsync(ws, calls);
        var done = await ws.ReceiveUntilAsync(frame => Says(frame, calls), "the page of the handler that ended");

        // Each call after the first left with the page as the answer before it had left it.
        Assert.All(carried.Skip(1).Index(), frame => Assert.True(Says(frame.Item, frame.Index + 1), frame.Item));
        Assert.DoesNotContain("jsInvokes", done, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Calls_a_handler_awaits_on_its_own_context_are_sent_by_the_repaint_around_each_continuation()
    {
        var plan = new CallsAwaitedApp.Plan(Calls: 2, KeepsContext: true);
        using var host = RaskTestHost.Create<CallsAwaitedApp>(services => services.AddSingleton(plan));
        using var ws = await PressedAsync(host, plan);

        var carried = await AnswerEachAsync(ws, calls: 2);
        var done = await ws.ReceiveUntilAsync(frame => Says(frame, 2), "the page of the handler that ended");

        Assert.True(Says(carried[1], 1), carried[1]);
        Assert.DoesNotContain("jsInvokes", done, StringComparison.Ordinal);
    }

    // The button pressed, the page the handler's budget shows read, and the slow thing the handler waited for ended.
    private static async Task<WebSocket> PressedAsync(RaskTestHost host, CallsAwaitedApp.Plan plan)
    {
        var page = await (await host.Http.GetAsync("/", TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = MarkupAssert.SessionId(page) }, ct: TestContext.Current.CancellationToken);
        await ws.SettledAsync();
        await ws.SendJsonAsync(new { id = MarkupAssert.RequireAttr(page, "data-rask-on-click") }, ct: TestContext.Current.CancellationToken);
        await ws.ReceiveUntilAsync(frame => frame.Contains("waiting", StringComparison.Ordinal), "the page the handler's budget shows");
        plan.Slow.SetResult();

        return ws;
    }

    // Reads the frame that carries each call and answers it, as a browser does. The frames, in order.
    private static async Task<List<string>> AnswerEachAsync(WebSocket ws, int calls)
    {
        List<string> carried = [];
        for (var call = 1; call <= calls; call++)
        {
            var name = CallsAwaitedApp.Name(call);
            var frame = await ws.ReceiveUntilAsync(
                sent => sent.Contains($"\"{name}\"", StringComparison.Ordinal), $"the frame that carries call {call}");
            carried.Add(frame);
            await ws.SendJsonAsync(new { type = "jsResult", id = CallId(frame), success = true }, ct: TestContext.Current.CancellationToken);
        }

        return carried;
    }

    private static bool Says(string frame, int answered) =>
        frame.Contains("answered " + answered.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static long CallId(string frame)
    {
        using var read = JsonDocument.Parse(frame);

        return read.RootElement.GetProperty("jsInvokes")[0].GetProperty("id").GetInt64();
    }
}
