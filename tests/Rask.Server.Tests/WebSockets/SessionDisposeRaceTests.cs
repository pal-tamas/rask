using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Regression test for the teardown race between LiveSession disposal and an in-flight render.
//
// LiveSessionStore.DisposeAsync (host shutdown) walks each session's component tree via
// ComponentLifecycle.DisposeComponentTreeAsync, which enumerates every component's
// PersistedChildren. A render fired from an async lifecycle continuation rebuilds those same
// child dictionaries under _renderLock (the swap+Clear in Component.BuildRenderTree). Disposal
// did not take _renderLock, so a render still draining on a thread-pool thread at shutdown raced
// the walk and intermittently threw "Collection was modified; enumeration operation may not
// execute" (observed flaking HandlerOrderingTests.TwoHandlers_AcrossMultipleRounds_NeverReorder).
//
// The fix makes Dispose/DisposeAsync acquire _renderLock around the walk, so disposal and render
// are mutually exclusive. This test gates a render in flight and asserts disposal blocks until the
// render releases the lock — deterministic where a raw stress loop would only catch it sometimes.
public class SessionDisposeRaceTests
{
    [Fact]
    public async Task DisposeAsync_WhileRenderInFlight_WaitsForRenderLock_AndDoesNotThrow()
    {
        GatedRenderApp.Reset();

        using var host = RaskTestHost.Create<GatedRenderApp>();
        try
        {
            var html = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
            var sessionId = MarkupAssert.SessionId(html);
            var handlerId = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"").Groups[1].Value;
            Assert.NotEmpty(handlerId);

            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            // Click the button: the resulting render enters Render() and parks there, holding the
            // session's _renderLock mid-walk.
            await ws.SendJsonAsync(new { id = handlerId });
            await GatedRenderApp.RenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Tear the store down (host-shutdown path) while that render is still in flight.
            var disposeTask = host.Store.DisposeAsync().AsTask();

            // With the fix, disposal waits on _renderLock and cannot make progress while the render
            // is gated. Pre-fix it ran straight into the racing tree walk.
            var completedEarly = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromMilliseconds(500)))
                                 == disposeTask;
            Assert.False(completedEarly, "DisposeAsync must wait for the in-flight render to release the render lock");

            // Release the render; disposal now completes cleanly with no collection-modified throw.
            GatedRenderApp.ReleaseRender.Set();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            // Never leave the gate latched, or the host's own teardown would deadlock.
            GatedRenderApp.ReleaseRender.Set();
        }
    }
}
