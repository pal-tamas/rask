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
