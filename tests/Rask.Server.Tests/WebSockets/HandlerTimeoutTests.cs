using System.Net.WebSockets;
using Rask.Server.Tests.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// RaskServerOptions.HandlerTimeout: a cooperative handler that threads EventCancellationToken into its
// async work is cancelled when the timeout elapses, so it unwinds instead of pinning the render lock.
[Collection("SessionGracePeriod")]
public class HandlerTimeoutTests
{
    [Fact]
    public async Task CooperativeHandler_PastTimeout_IsCancelled_AndMetered_SessionSurvives()
    {
        var prev = RaskEndpointExtensions.HandlerTimeout;
        RaskEndpointExtensions.HandlerTimeout = TimeSpan.FromMilliseconds(300);
        CooperativeTimeoutApp.Cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = RaskTestHost.Create<CooperativeTimeoutApp>();
            using var capture = MeterCapture.For(host.Store.Metrics!.Meter);

            var html = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
            var sessionId = Markup.SessionId(html);
            var handlerId = Markup.FirstHandlerId(html);

            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            // Fire the slow handler. It awaits a 30 s delay observing EventCancellationToken, so without
            // the timeout this would hang for 30 s; with it, the handler is cancelled within ~300 ms.
            await ws.SendJsonAsync(new { id = handlerId });

            // The handler observed cancellation well before its 30 s delay would elapse.
            var observed = await Task.WhenAny(
                CooperativeTimeoutApp.Cancelled.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(CooperativeTimeoutApp.Cancelled.Task, observed);
            Assert.True(await CooperativeTimeoutApp.Cancelled.Task);

            // The timeout was metered, and the session/socket survived.
            var metered = await WaitUntil(
                () => capture.Counter("rask.handlers.timedout") >= 1, TimeSpan.FromSeconds(2));
            Assert.True(metered, "expected rask.handlers.timedout to increment");
            Assert.Equal(WebSocketState.Open, ws.State);
        }
        finally
        {
            RaskEndpointExtensions.HandlerTimeout = prev;
            CooperativeTimeoutApp.Cancelled.TrySetResult(false);
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
