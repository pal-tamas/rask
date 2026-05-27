using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

// Regression: an unmounted component's StateHasChanged must be a no-op. Pre-fix,
// a long-running OnMountAsync (e.g. LiveTicker's poll loop) left in-flight
// LifecycleSyncContext continuations that, on cancellation, still called
// StateHasChanged on the disposed component — queuing ghost session renders
// against the newly-mounted page. The visible symptom was 11+ spurious
// OnRendered(firstRender:false) entries on a freshly-mounted LifecycleProbe
// after navigating from /realtime/BTC → /lifecycle.
public class StateHasChangedAfterUnmountTests
{
    [Fact]
    public void StateHasChanged_AfterUnmount_IsNoop()
    {
        var handle = new RecordingHandle();
        var c = new TrivialComponent { RenderHandle = handle };
        c.RaiseLifecycleBeforeRender(true);

        ComponentLifecycle.DisposeComponentTree(c);

        c.StateHasChanged();

        Assert.Equal(0, handle.RequestRenderCount);
    }

    [Fact]
    public async Task StateHasChangedAsync_AfterUnmount_IsNoop()
    {
        var handle = new RecordingHandle();
        var c = new TrivialComponent { RenderHandle = handle };
        c.RaiseLifecycleBeforeRender(true);

        ComponentLifecycle.DisposeComponentTree(c);

        await c.StateHasChangedAsync();

        Assert.Equal(0, handle.RequestRenderCount);
    }

    [Fact]
    public void StateHasChanged_BeforeUnmount_StillRenders()
    {
        // Sanity check: the unmount guard must not affect live components.
        var handle = new RecordingHandle();
        var c = new TrivialComponent { RenderHandle = handle };
        c.RaiseLifecycleBeforeRender(true);

        c.StateHasChanged();

        Assert.Equal(1, handle.RequestRenderCount);
    }

    [Fact]
    public async Task LateLifecycleSyncContextPost_AfterUnmount_DoesNotQueueRender()
    {
        // Models the exact LiveTicker-→-Lifecycle regression: an OnMountAsync
        // captures its continuation via LifecycleSyncContext; the component is
        // unmounted while the gate is still pending; the gate then resolves.
        // The settling continuation must NOT queue a session render against the
        // disposed component.
        var handle = new RecordingHandle();
        var c = new GatedAsyncMountComponent { RenderHandle = handle };
        c.RaiseLifecycleBeforeRender(true);

        // Wait for OnMountAsync to suspend on the gate.
        await c.Started.Task;
        var renderCountBefore = handle.RequestRenderCount;

        ComponentLifecycle.DisposeComponentTree(c);

        // Release the gate AFTER unmount — the continuation runs on a thread-pool
        // thread inside LifecycleSyncContext.Post, which historically called
        // _component.StateHasChanged() unconditionally.
        c.Gate.SetResult();
        await Task.Delay(50);

        Assert.Equal(renderCountBefore, handle.RequestRenderCount);
    }

    private sealed class TrivialComponent : Component
    {
        protected override RenderResult Render() => Span();
    }

    private sealed class GatedAsyncMountComponent : Component
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task OnMountAsync()
        {
            Started.SetResult();
            await Gate.Task;
        }

        protected override RenderResult Render() => Span();
    }

    private sealed class RecordingHandle : IRenderHandle
    {
        public int RequestRenderCount;
        public int RequestPublishRenderCount;

        public Task RequestRenderAsync()
        {
            Interlocked.Increment(ref RequestRenderCount);
            return Task.CompletedTask;
        }

        public Task RequestPublishRenderAsync()
        {
            Interlocked.Increment(ref RequestPublishRenderCount);
            return Task.CompletedTask;
        }
    }
}
