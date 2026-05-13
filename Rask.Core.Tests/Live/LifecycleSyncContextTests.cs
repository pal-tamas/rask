using Rask.Core.Components;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class LifecycleSyncContextTests
{
    [Fact]
    public void Send_ExecutesInline()
    {
        var component = new RecordingComponent();
        var ctx = new LifecycleSyncContext(component);
        var thread = -1;

        ctx.Send(_ => thread = Environment.CurrentManagedThreadId, null);

        Assert.Equal(Environment.CurrentManagedThreadId, thread);
        Assert.Equal(0, component.RenderRequests);
    }

    [Fact]
    public async Task Post_TriggersStateHasChanged_AfterCallback()
    {
        var component = new RecordingComponent();
        var ctx = new LifecycleSyncContext(component);
        var ran = new TaskCompletionSource();

        ctx.Post(_ => ran.SetResult(), null);
        await ran.Task;

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (component.RenderRequests == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, component.RenderRequests);
    }

    [Fact]
    public async Task Post_SuppressesExecutionContextFlow()
    {
        var component = new RecordingComponent();
        var ctx = new LifecycleSyncContext(component);
        var asyncLocal = new AsyncLocal<string?> { Value = "outer" };
        string? observed = null;
        var done = new TaskCompletionSource();

        ctx.Post(_ =>
        {
            observed = asyncLocal.Value;
            done.SetResult();
        }, null);

        await done.Task;

        Assert.Null(observed);
    }

    [Fact]
    public void CreateCopy_ReturnsIndependentInstance()
    {
        var component = new RecordingComponent();
        var ctx = new LifecycleSyncContext(component);

        var copy = ctx.CreateCopy();

        Assert.NotSame(ctx, copy);
        Assert.IsType<LifecycleSyncContext>(copy);
    }

    private sealed class RecordingComponent : Component, IRenderHandle
    {
        public int RenderRequests;

        public RecordingComponent() => RenderHandle = this;

        public Task RequestRenderAsync()
        {
            Interlocked.Increment(ref RenderRequests);
            return Task.CompletedTask;
        }

        protected override Component Render() => Span();
    }
}
