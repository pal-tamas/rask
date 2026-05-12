using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Tests.Lifecycle;

public class InitializationTests
{
    [Fact]
    public void OnInitialized_FiresOnce_AcrossManyRenders()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 5; i++)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved);
        }

        Assert.Equal(1, c.InitializedCount);
        Assert.Equal(1, c.InitializedAsyncCount);
    }

    [Fact]
    public void OnParametersSet_FiresEveryRender()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 3; i++)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved);
        }

        Assert.Equal(3, c.ParametersSetCount);
        Assert.Equal(3, c.ParametersSetAsyncCount);
    }

    [Fact]
    public void OnInitialized_FiresBeforeOnParametersSet()
    {
        var order = new List<string>();
        var c = new OrderRecorder(order);
        var sp = new ServiceCollection().BuildServiceProvider();

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved);
        }

        Assert.Equal(new[] { "init", "params" }, order);
    }

    [Fact]
    public async Task OnInitializedAsync_IncompleteTask_TriggersRerenderOnCompletion()
    {
        var handle = new RecordingRenderHandle();
        var tcs = new TaskCompletionSource();
        var c = new LifecycleTrackingComponent { RenderHandle = handle, OnInitializedAsyncImpl = () => tcs.Task };
        var sp = new ServiceCollection().BuildServiceProvider();

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved);
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
        protected override void OnInitialized() => _order.Add("init");
        protected override void OnParametersSet() => _order.Add("params");
        public override Component Render() => this;
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
