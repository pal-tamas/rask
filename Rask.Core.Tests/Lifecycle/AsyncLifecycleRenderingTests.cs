using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class AsyncLifecycleRenderingTests
{
    [Fact]
    public async Task OnMountAsync_TriggersStateHasChanged_AfterEachAwait()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var handle = new RecordingHandle();
        var c = new ProgressiveComponent { RenderHandle = handle };

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        await c.Started.Task;
        await c.Step1.Task;
        await c.Step2.Task;
        await c.Done.Task;
        // Drain pending continuations to allow Post→StateHasChanged dispatches to complete.
        await Task.Delay(50);

        Assert.True(handle.RequestRenderCount >= 2,
            $"expected progressive renders after awaits, got {handle.RequestRenderCount}");
    }

    [Fact]
    public async Task OnMountAsync_NoAwaits_DoesNotTriggerExtraRender()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var handle = new RecordingHandle();
        var c = new SyncCompletingComponent { RenderHandle = handle };

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        await Task.Delay(20);
        Assert.Equal(0, handle.RequestRenderCount);
    }

    [Fact]
    public async Task OnRenderedAsync_AwaitCompletes_TriggersRerender()
    {
        // Regression: OnRenderedAsync used to fire-and-forget with rerender=false, so any
        // state mutation after an `await` inside it was invisible to the UI until the user
        // explicitly called StateHasChanged. Flipped to rerender=true so this works the
        // same way as OnMountAsync / event handlers — matches CLAUDE.md's documented
        // async lifecycle ("each await triggers a post-continuation StateHasChanged plus
        // a terminal re-render on task completion").
        var sp = new ServiceCollection().BuildServiceProvider();
        var handle = new RecordingHandle();
        var c = new RenderedAsyncProbe { RenderHandle = handle };

        // RenderAsLiveRoot drives the render walk and triggers OnRendered / OnRenderedAsync.
        c.RenderAsLiveRoot(sp);

        // After the initial render, OnRenderedAsync(true) is awaiting `Gate`. No auto-render
        // has fired yet (continuation hasn't run).
        await Task.Delay(20);
        var beforeGate = handle.RequestRenderCount;

        // Release the gate; the continuation completion path should auto-trigger a render.
        c.Gate.SetResult();
        await Task.Delay(50);

        Assert.True(handle.RequestRenderCount > beforeGate,
            $"expected re-render after OnRenderedAsync continuation; before={beforeGate} after={handle.RequestRenderCount}");
    }

    [Fact]
    public async Task OnRenderedAsync_GuardedByFirstRender_DoesNotLoop()
    {
        // The canonical `if (!firstRender) return;` pattern completes synchronously on
        // subsequent renders → ScheduleAsyncContinuation early-outs (t.IsCompleted) →
        // no infinite render loop even with rerender=true.
        var sp = new ServiceCollection().BuildServiceProvider();
        var handle = new RecordingHandle();
        var c = new RenderedAsyncProbe { RenderHandle = handle };

        c.RenderAsLiveRoot(sp);
        c.Gate.SetResult();
        await Task.Delay(50);

        // Drive a few more explicit renders. OnRenderedAsync(false) is a no-op
        // (synchronously completed), so it should NOT keep requesting renders.
        c.RenderAsLiveRoot(sp);
        c.RenderAsLiveRoot(sp);
        await Task.Delay(50);

        Assert.True(handle.RequestRenderCount < 10,
            $"render storm detected: {handle.RequestRenderCount} renders");
    }

    private sealed class RenderedAsyncProbe : Component
    {
        public TaskCompletionSource Gate { get; } = new();

        protected override async Task OnRenderedAsync(bool firstRender)
        {
            if (!firstRender) return;
            await Gate.Task;
        }

        protected override Component Render() => Span()[Text("probe")];
    }

    private sealed class ProgressiveComponent : Component
    {
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource Step1 { get; } = new();
        public TaskCompletionSource Step2 { get; } = new();
        public TaskCompletionSource Done { get; } = new();

        protected override async Task OnMountAsync()
        {
            Started.TrySetResult();
            await Task.Yield();
            Step1.TrySetResult();
            await Task.Yield();
            Step2.TrySetResult();
            Done.TrySetResult();
        }

        protected override Component Render() => this;
    }

    private sealed class SyncCompletingComponent : Component
    {
        protected override Task OnMountAsync() => Task.CompletedTask;
        protected override Component Render() => this;
    }

    private sealed class RecordingHandle : IRenderHandle
    {
        public int RequestRenderCount;

        public Task RequestRenderAsync()
        {
            Interlocked.Increment(ref RequestRenderCount);
            return Task.CompletedTask;
        }
    }
}
