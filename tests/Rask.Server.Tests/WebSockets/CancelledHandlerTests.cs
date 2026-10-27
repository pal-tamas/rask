using Rask.Core;
using Rask.Server.Tests.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// A handler whose dispatch is cancelled — its timeout, or its connection closing — ends as a cancelled one, however
// what it awaited says so: EF over SQL Server reports a cancelled query as an InvalidOperationException (#1256).
public sealed class CancelledHandlerTests
{
    [Fact]
    public async Task A_handler_whose_query_says_its_timeout_is_a_failure_is_recorded_as_timed_out_not_faulted()
    {
        EfLikeHandlerApp.Parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = RaskTestHost.Create<EfLikeHandlerApp>(
            configureServer: o => o.HandlerTimeout = TimeSpan.FromMilliseconds(200));
        using var meters = MeterCapture.For(host.Store.Metrics!.Meter);
        var html = await host.Http.GetStringAsync("/start", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(html);
        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        var session = host.Store.Get(sessionId)!;

        await ws.SendJsonAsync(new { id = MarkupAssert.FirstHandlerId(html) }, ct: TestContext.Current.CancellationToken);
        await EfLikeHandlerApp.Parked.Task.WaitAsync(LiveFrames.HangCeiling, TestContext.Current.CancellationToken);
        await WaitFor.True(() => session.PendingHandlers == 0, "the dispatch has ended");
        await session.LastHandlerTask;

        Assert.Equal(1, meters.Counter("rask.handlers.timedout"));
        Assert.Equal(0, meters.Counter("rask.handlers.faulted"));
        Assert.Equal(System.Net.WebSockets.WebSocketState.Open, ws.State);
    }
}

/// <summary>A handler that reads as EF over SQL Server does: cancelled, it throws an InvalidOperationException.</summary>
public sealed partial class EfLikeHandlerApp : Component
{
    public static TaskCompletionSource Parked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Component? HeadAssets => Markup.Title["cancelled"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Button
            .OnClick(async () =>
        {
            Parked.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, CancellationToken);
            }
            catch (OperationCanceledException cancelled)
            {
                throw new InvalidOperationException(
                    "An exception has been raised that is likely due to a transient failure.", cancelled);
            }
        })["read"]
    ];
}
