using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class AsyncLifecycleRenderingTests
{
    [Fact]
    public async Task OnMountAsync_TriggersStateHasChanged_AfterEachAwait()
    {
        var sp = RenderHarness.EmptyServices();
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
    public async Task OnMountAsync_TrailingAwait_FiresExactlyOneRender()
    {
        // Regression: when the last statement of an async lifecycle hook is an await,
        // the LifecycleSyncContext.Post path AND the terminal ContinueWith both used
        // to fire StateHasChanged back-to-back — observable on the lifecycle showcase
        // page as the Render #N badge jumping from #1 to #3 instead of #1 to #2 after
        // OnMountAsync's await completed. Post sets PostFired before launching its
        // Task.Run, so the terminal callback (which fires synchronously from inside
        // d(state) when the user method's last statement is an await) reads the flag
        // and short-circuits.
        var sp = RenderHarness.EmptyServices();
        var handle = new RecordingHandle();
        var c = new SingleAwaitComponent { RenderHandle = handle };

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        await c.Done.Task;
        await Task.Delay(50);

        Assert.Equal(1, handle.RequestRenderCount);
    }

    [Fact]
    public async Task OnMountAsync_ConfigureAwaitFalse_StillFiresTerminalRender()
    {
        // When the user uses ConfigureAwait(false) the continuation does NOT route
        // through LifecycleSyncContext.Post, so PostFired stays false and the terminal
        // ContinueWith MUST still fire its StateHasChanged — otherwise such hooks
        // would never trigger a follow-up render at all.
        var sp = RenderHarness.EmptyServices();
        var handle = new RecordingHandle();
        var c = new ConfigureAwaitFalseComponent { RenderHandle = handle };

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        await c.Done.Task;
        await Task.Delay(50);

        Assert.Equal(1, handle.RequestRenderCount);
    }

    [Fact]
    public async Task OnMountAsync_NoAwaits_DoesNotTriggerExtraRender()
    {
        var sp = RenderHarness.EmptyServices();
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
        // OnRenderedAsync auto-rerenders on continuation completion — same ergonomics
        // as OnMountAsync, so users can `_x = await ...;` without calling
        // StateHasChanged. The continuation routes through RequestPublishRenderAsync
        // so the resulting walk is loop-safe (already-rendered components skip the
        // hook).
        var sp = RenderHarness.EmptyServices();
        var handle = new RecordingHandle();
        var c = new RenderedAsyncProbe { RenderHandle = handle };

        c.RenderAsLiveRoot(sp);

        await Task.Delay(20);
        var beforeGate = handle.RequestPublishRenderCount;

        c.Gate.SetResult();
        await Task.Delay(50);

        Assert.True(handle.RequestPublishRenderCount > beforeGate,
            $"expected auto-rerender after OnRenderedAsync continuation; " +
            $"publish-before={beforeGate} publish-after={handle.RequestPublishRenderCount}");
    }

    [Fact]
    public async Task OnRenderedAsync_AwaitsEveryRender_DoesNotLoop()
    {
        // Regression for the render-storm leak: a component that unconditionally awaits
        // something in OnRenderedAsync (without an `if (!firstRender) return;` guard)
        // used to drive an infinite render loop. The continuation's auto-rerender goes
        // through RequestPublishRenderAsync which flags the resulting walk as publishOnly,
        // so already-rendered components don't re-enter their OnRenderedAsync hook on
        // the publish frame — no fresh continuation, no fresh request → loop broken.
        var sp = RenderHarness.EmptyServices();
        var handle = new RecordingHandle();
        var c = new AlwaysAwaitsProbe { RenderHandle = handle };

        c.RenderAsLiveRoot(sp);
        c.Release();
        await Task.Delay(50);

        c.RenderAsLiveRoot(sp);
        c.Release();
        await Task.Delay(50);

        Assert.True(handle.RequestRenderCount < 5,
            $"render storm detected: {handle.RequestRenderCount} renders");
    }

    [Fact]
    public async Task OnRenderedAsync_MultipleComponents_DoNotCascade()
    {
        // The structurally interesting regression: A and B both have unguarded
        // OnRenderedAsync awaits. Per-component suppression isn't enough — A's
        // continuation triggers a render walk, which re-fires B's OnRenderedAsync,
        // which on completion triggers ANOTHER walk that re-fires A's, ad infinitum.
        // The fix is the publishOnly walk mode: the continuation's render walks but
        // skips OnRenderedAsync on every already-rendered component (not just the
        // originating one), so the cascade can't kindle.
        var sp = RenderHarness.EmptyServices();
        var handle = new RecordingHandle();
        var root = new MultiAwaitProbe { RenderHandle = handle };

        root.RenderAsLiveRoot(sp);
        root.ReleaseAll();
        await Task.Delay(150);

        Assert.True(handle.RequestPublishRenderCount < 8,
            $"cascade detected: {handle.RequestPublishRenderCount} publish renders");
        Assert.True(root.AOnRenderedCount < 5,
            $"A's OnRenderedAsync re-fired {root.AOnRenderedCount} times");
        Assert.True(root.BOnRenderedCount < 5,
            $"B's OnRenderedAsync re-fired {root.BOnRenderedCount} times");
    }

    private sealed class RenderedAsyncProbe : Component
    {
        public TaskCompletionSource Gate { get; } = new();

        protected override async Task OnRenderedAsync(bool firstRender)
        {
            if (!firstRender) return;
            await Gate.Task;
        }

        protected override RenderResult Render() => Span()[Text("probe")];
    }

    private sealed class AlwaysAwaitsProbe : Component
    {
        private TaskCompletionSource _gate = new();

        public void Release()
        {
            var prev = Interlocked.Exchange(ref _gate, new TaskCompletionSource());
            prev.TrySetResult();
        }

        protected override Task OnRenderedAsync(bool firstRender) => _gate.Task;

        protected override RenderResult Render() => Span()[Text("probe")];
    }

    // Two-component probe wired into one render tree. A and B each have their own
    // unguarded OnRenderedAsync await. Calls to ReleaseAll complete both gates so
    // both continuations fire, exercising the multi-component cascade path.
    private sealed class MultiAwaitProbe : Component
    {
        public int AOnRenderedCount;
        public int BOnRenderedCount;
        private readonly AlwaysAwaitsProbe _a = new();
        private readonly AlwaysAwaitsProbe _b = new();

        public void ReleaseAll()
        {
            _a.Release();
            _b.Release();
        }

        protected override RenderResult Render() => Div()[_a, _b];

        protected override Task OnRenderedAsync(bool firstRender)
        {
            // Tally hook re-entries on the root too — if the publishOnly walk is broken,
            // this counter pegs at hundreds.
            if (firstRender) AOnRenderedCount++;
            else BOnRenderedCount++;
            return Task.CompletedTask;
        }
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

        protected override RenderResult Render() => this;
    }

    private sealed class SyncCompletingComponent : Component
    {
        protected override Task OnMountAsync() => Task.CompletedTask;
        protected override RenderResult Render() => this;
    }

    private sealed class SingleAwaitComponent : Component
    {
        public TaskCompletionSource Done { get; } = new();

        protected override async Task OnMountAsync()
        {
            await Task.Yield();
            Done.TrySetResult();
        }

        protected override RenderResult Render() => this;
    }

    private sealed class ConfigureAwaitFalseComponent : Component
    {
        public TaskCompletionSource Done { get; } = new();

        protected override async Task OnMountAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            Done.TrySetResult();
        }

        protected override RenderResult Render() => this;
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
