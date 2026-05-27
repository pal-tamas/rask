using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class MountTests
{
    [Fact]
    public void OnMount_FiresOnce_AcrossManyRenders()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 5; i++)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        Assert.Equal(1, c.MountCount);
        Assert.Equal(1, c.MountAsyncCount);
    }

    [Fact]
    public void OnPropsChanged_FiresEveryRenderWhenPropsChange()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 3; i++)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        Assert.Equal(3, c.PropsChangedCount);
        Assert.Equal(3, c.PropsChangedAsyncCount);
    }

    [Fact]
    public void OnPropsChanged_FiresOnceWhenPropsUnchanged()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 3; i++)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, false);
        }

        Assert.Equal(1, c.PropsChangedCount);
        Assert.Equal(1, c.PropsChangedAsyncCount);
    }

    [Fact]
    public void OnMount_FiresBeforeOnPropsChanged()
    {
        var order = new List<string>();
        var c = new OrderRecorder(order);
        var sp = new ServiceCollection().BuildServiceProvider();

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        Assert.Equal(new[] { "mount", "props" }, order);
    }

    [Fact]
    public async Task OnMountAsync_IncompleteTask_TriggersRerenderOnCompletion()
    {
        var handle = new RecordingRenderHandle();
        var tcs = new TaskCompletionSource();
        var c = new LifecycleTrackingComponent { RenderHandle = handle, OnMountAsyncImpl = () => tcs.Task };
        var sp = new ServiceCollection().BuildServiceProvider();

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
        }

        Assert.Equal(0, handle.RequestRenderCount);
        tcs.SetResult();
        await Task.Yield();
        Assert.Equal(1, handle.RequestRenderCount);
    }

    private sealed class OrderRecorder : Component
    {
        private readonly List<string> _order;
        public OrderRecorder(List<string> order) => _order = order;
        protected override void OnMount() => _order.Add("mount");
        protected override void OnPropsChanged() => _order.Add("props");
        protected override RenderResult Render() => this;
    }

    private sealed class RecordingRenderHandle : IRenderHandle
    {
        public int RequestRenderCount;

        public Task RequestRenderAsync()
        {
            Interlocked.Increment(ref RequestRenderCount);
            return Task.CompletedTask;
        }
    }
}
