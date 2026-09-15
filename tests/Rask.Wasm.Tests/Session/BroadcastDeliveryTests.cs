using Rask.Core.Messaging;
using Rask.Wasm.Tests.Infrastructure;
using static Rask.Wasm.Tests.Infrastructure.WasmSessionHarness;

namespace Rask.Wasm.Tests.Session;

// #1061: in a browser-WASM app a broadcast stays in the tab, and is delivered the way an event is — under the session's
// dispatch lock, then one render.
[Collection("WasmSession")]
public sealed class BroadcastDeliveryTests
{
    private static readonly Topic<int> Ticks = new("ticks");

    [Fact]
    public async Task A_publish_runs_the_subscriber_in_the_session_and_renders_it()
    {
        var (session, _) = NewSession<RenderCountingApp>();
        await session.InitialRenderAsync();
        var app = FindApp(session);
        var hub = new BroadcastHub();
        var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Subscribe(app, Ticks, tick => received.TrySetResult(tick));
        var before = app.RenderCount;

        await hub.PublishAsync(Ticks, 7);

        Assert.Equal(7, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await WaitUntilAsync(() => app.RenderCount > before);
    }

    [Fact]
    public async Task A_delivery_runs_its_work_in_scope_and_releases_the_scope_after()
    {
        var (session, _) = NewSession<RenderCountingApp>();
        await session.InitialRenderAsync();
        var ran = false;

        var delivery = session.DeliverInScopeAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await delivery.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(ran);
        Assert.False(session.InHandlerScope);
    }

    private static RenderCountingApp FindApp(WasmLiveSession session) =>
        (RenderCountingApp)session.View.PersistedChildren.Values.Single(c => c is RenderCountingApp);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(10);
        }
    }
}
