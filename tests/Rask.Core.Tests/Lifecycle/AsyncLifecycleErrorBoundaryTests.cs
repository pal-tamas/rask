using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

[Collection("ConsoleRedirect")]
public class AsyncLifecycleErrorBoundaryTests
{
    [Fact]
    public async Task OnMountAsync_Throws_TripsAncestorBoundary()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.MountAsync);
        var boundary = ErrorBoundary();
        boundary.SetProps(new Component[] { child }, null);

        // Drive a render so the descendant gets stamped with its Boundary, then its
        // OnMountAsync fires. The faulted Task continuation routes through Boundary.Trip.
        using (LiveRenderContext.Begin(boundary, sp))
        {
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(true);
            await child.Fault.Task;
            await DrainContinuations();
        }

        Assert.NotNull(boundary.Error);
        Assert.Equal("mount-async", boundary.Error!.Message);
    }

    [Fact]
    public async Task OnPropsChangedAsync_Throws_TripsAncestorBoundary()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.PropsAsync);
        var boundary = ErrorBoundary();
        boundary.SetProps(new Component[] { child }, null);

        using (LiveRenderContext.Begin(boundary, sp))
        {
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(true);
            await child.Fault.Task;
            await DrainContinuations();
        }

        Assert.NotNull(boundary.Error);
        Assert.Equal("props-async", boundary.Error!.Message);
    }

    [Fact]
    public async Task AsyncFault_NoBoundary_LogsToConsoleError()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.MountAsync);
        // No boundary — the existing log-and-swallow path should fire.

        var origErr = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            using (LiveRenderContext.Begin(child, sp))
            {
                child.RaiseLifecycleBeforeRender(true);
                await child.Fault.Task;
                await DrainContinuations();
            }
        }
        finally
        {
            Console.SetError(origErr);
        }

        Assert.Contains("mount-async", sw.ToString());
        Assert.Contains("FaultingComponent", sw.ToString());
    }

    [Fact]
    public async Task TripFromAsyncFault_RequestsRenderViaHandle()
    {
        // Boundary.Trip calls StateHasChanged which uses RenderHandle.RequestRenderAsync.
        // Without a render request, the live root would never re-render with the fallback.
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.MountAsync);
        var boundary = ErrorBoundary();
        var handle = new RecordingHandle();
        boundary.RenderHandle = handle;
        boundary.SetProps(new Component[] { child }, null);

        using (LiveRenderContext.Begin(boundary, sp))
        {
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(true);
            await child.Fault.Task;
            await DrainContinuations();
        }

        Assert.True(handle.RequestRenderCount >= 1,
            $"expected boundary trip to request render, got {handle.RequestRenderCount}");
    }

    private static async Task DrainContinuations()
    {
        // The async-fault path uses TaskContinuationOptions.ExecuteSynchronously, but the
        // initial Task.Yield inside FaultingComponent hops onto the threadpool so we need
        // a small delay to let the continuation observe the fault.
        for (var i = 0; i < 5; i++)
        {
            await Task.Yield();
            await Task.Delay(10);
        }
    }

    private enum FaultPoint
    {
        MountAsync,
        PropsAsync
    }

    private sealed class FaultingComponent : Component
    {
        private readonly FaultPoint _faultPoint;

        public FaultingComponent(FaultPoint faultOn) => _faultPoint = faultOn;
        public TaskCompletionSource Fault { get; } = new();

        protected override async Task OnMountAsync()
        {
            if (_faultPoint != FaultPoint.MountAsync)
            {
                return;
            }

            await Task.Yield();
            Fault.TrySetResult();
            throw new InvalidOperationException("mount-async");
        }

        protected override async Task OnPropsChangedAsync()
        {
            if (_faultPoint != FaultPoint.PropsAsync)
            {
                return;
            }

            await Task.Yield();
            Fault.TrySetResult();
            throw new InvalidOperationException("props-async");
        }

        protected override Component? Render() => Span[Text.Value("loading")];
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
