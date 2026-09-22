using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class MountTests
{
    [Fact]
    public void OnMount_fires_once_across_many_renders()
    {
        var sp = RenderHarness.EmptyServices();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 5; i++)
        {
            using (RenderHarness.Render(c, sp)) { }
        }

        Assert.Equal(1, c.MountCount);
        Assert.Equal(1, c.MountAsyncCount);
    }

    [Fact]
    public void OnPropsChanged_fires_every_render_when_the_props_change()
    {
        var sp = RenderHarness.EmptyServices();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 3; i++)
        {
            using (RenderHarness.Render(c, sp)) { }
        }

        Assert.Equal(3, c.PropsChangedCount);
        Assert.Equal(3, c.PropsChangedAsyncCount);
    }

    [Fact]
    public void OnPropsChanged_fires_once_when_the_props_are_unchanged()
    {
        var sp = RenderHarness.EmptyServices();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 3; i++)
        {
            using (RenderHarness.Render(c, sp, false)) { }
        }

        Assert.Equal(1, c.PropsChangedCount);
        Assert.Equal(1, c.PropsChangedAsyncCount);
    }

    [Fact]
    public void OnMount_fires_before_the_first_OnPropsChanged()
    {
        var order = new List<string>();
        var c = new OrderRecorder(order);
        var sp = RenderHarness.EmptyServices();

        using (RenderHarness.Render(c, sp)) { }

        Assert.Equal(new[] { "mount", "props" }, order);
    }

    [Fact]
    public async Task An_incomplete_OnMountAsync_task_triggers_a_rerender_on_completion()
    {
        var handle = new RecordingRenderHandle();
        var tcs = new TaskCompletionSource();
        var c = new LifecycleTrackingComponent { RenderHandle = handle, OnMountAsyncImpl = () => tcs.Task };
        var sp = RenderHarness.EmptyServices();

        using (RenderHarness.Render(c, sp)) { }

        Assert.Equal(0, handle.RequestRenderCount);
        tcs.SetResult();
        await Task.Yield();
        Assert.Equal(1, handle.RequestRenderCount);
    }

    private sealed class OrderRecorder : Component
    {
        private readonly List<string> _order;
        public OrderRecorder(List<string> order) => _order = order;
        protected override Task OnMount()
        {
            _order.Add("mount");
            return Task.CompletedTask;
        }
        protected override Task OnUpdated()
        {
            _order.Add("props");
            return Task.CompletedTask;
        }
        protected override Component? Render() => this;
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
