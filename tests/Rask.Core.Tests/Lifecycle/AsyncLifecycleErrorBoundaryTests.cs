using System.Text;
using Rask.Core.Live;
using Rask.TestSupport;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

[Collection("ConsoleRedirect")]
public partial class AsyncLifecycleErrorBoundaryTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task OnMountAsync_Throws_TripsAncestorBoundary()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.MountAsync);
        var boundary = ErrorBoundary.Value;
        boundary.SetProps(new Component[] { child }, null);

        // Drive a render so the descendant gets stamped with its Boundary, then its
        // OnMountAsync fires. The faulted Task continuation routes through Boundary.Trip.
        using (LiveRenderContext.Begin(boundary, sp))
        {
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(true);
            await child.Fault.Task;
            await WaitFor.True(() => boundary.Error is not null, Budget, "the boundary to trip");
        }

        Assert.Equal("mount-async", boundary.Error!.Message);
    }

    [Fact]
    public async Task OnPropsChangedAsync_Throws_TripsAncestorBoundary()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.PropsAsync);
        var boundary = ErrorBoundary.Value;
        boundary.SetProps(new Component[] { child }, null);

        using (LiveRenderContext.Begin(boundary, sp))
        {
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(true);
            await child.Fault.Task;
            await WaitFor.True(() => boundary.Error is not null, Budget, "the boundary to trip");
        }

        Assert.Equal("props-async", boundary.Error!.Message);
    }

    [Fact]
    public async Task AsyncFault_NoBoundary_LogsToConsoleError()
    {
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.MountAsync);
        // No boundary — the existing log-and-swallow path should fire.

        var origErr = Console.Error;
        // The logging happens on the threadpool continuation while this test reads the buffer, so the
        // writer has to be safe for a concurrent write + snapshot — StringWriter is not.
        var sw = new LockedWriter();
        Console.SetError(sw);
        try
        {
            using (LiveRenderContext.Begin(child, sp))
            {
                child.RaiseLifecycleBeforeRender(true);
                await child.Fault.Task;
                await WaitFor.True(() => sw.Text.Contains("mount-async", StringComparison.Ordinal),
                    Budget, "the fault to reach Console.Error");
            }
        }
        finally
        {
            Console.SetError(origErr);
        }

        Assert.Contains("FaultingComponent", sw.Text);
    }

    [Fact]
    public async Task TripFromAsyncFault_RequestsRenderViaHandle()
    {
        // Boundary.Trip calls StateHasChanged which uses RenderHandle.RequestRenderAsync.
        // Without a render request, the live root would never re-render with the fallback.
        var sp = RenderHarness.EmptyServices();
        var child = new FaultingComponent(FaultPoint.MountAsync);
        var boundary = ErrorBoundary.Value;
        var handle = new RecordingHandle();
        boundary.RenderHandle = handle;
        boundary.SetProps(new Component[] { child }, null);

        using (LiveRenderContext.Begin(boundary, sp))
        {
            _ = boundary.ToHtml();
            child.RaiseLifecycleBeforeRender(true);
            await child.Fault.Task;
            await handle.Requested.Task.WaitAsync(Budget);
        }

        Assert.True(handle.RequestRenderCount >= 1,
            $"expected boundary trip to request render, got {handle.RequestRenderCount}");
    }

    // The async-fault path uses TaskContinuationOptions.ExecuteSynchronously, but the initial
    // Task.Yield inside FaultingComponent hops onto the threadpool, so the continuation lands
    // whenever the pool gets to it. These waits used to be a fixed 50ms of Task.Delay, which is not a
    // synchronisation primitive: on a loaded machine the pool had not run it yet when the assert read
    // the result, and the gate failed on a diff that could not have caused it (#769). Waiting for the
    // outcome is fast when the pool is idle and patient when it is not, so the budget can be generous.
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

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

        // Signalled on the first render request, so the test awaits the event rather than a duration.
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestRenderAsync()
        {
            Interlocked.Increment(ref RequestRenderCount);
            Requested.TrySetResult();
            return Task.CompletedTask;
        }
    }

    /// <summary>A <see cref="TextWriter" /> whose buffer can be read while another thread writes to it.</summary>
    private sealed class LockedWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get { lock (_buffer) { return _buffer.ToString(); } }
        }

        public override void Write(char value)
        {
            lock (_buffer) { _buffer.Append(value); }
        }

        public override void Write(string? value)
        {
            lock (_buffer) { _buffer.Append(value); }
        }

        public override void WriteLine(string? value)
        {
            lock (_buffer) { _buffer.AppendLine(value); }
        }
    }
}
