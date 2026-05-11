using Rask.Core.Live;
using Rask.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Tests.Lifecycle;

public class ShouldRenderTests
{
    [Fact]
    public void ShouldRenderFalse_OnSubsequentRender_SkipsRenderAndReusesCache()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var child = new LifecycleTrackingComponent();
        var root = new ChildHost(child);
        var session = new LiveSession("test", root, scope);

        var first = session.View.RenderAsLiveRoot(scope.ServiceProvider);
        Assert.Equal(1, child.RenderCount);
        Assert.Contains("r1", first);

        child.ShouldRenderFunc = () => false;
        var second = session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(1, child.RenderCount);
        Assert.Contains("r1", second);
    }

    [Fact]
    public void ShouldRenderTrue_OnSubsequentRender_CallsRenderAgain()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var child = new LifecycleTrackingComponent();
        var root = new ChildHost(child);
        var session = new LiveSession("test", root, scope);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(2, child.RenderCount);
    }

    [Fact]
    public void ShouldRender_NotConsultedOnFirstRender()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var child = new LifecycleTrackingComponent { ShouldRenderFunc = () => false };
        var root = new ChildHost(child);
        var session = new LiveSession("test", root, scope);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(1, child.RenderCount);
    }

    private sealed class ChildHost : Component
    {
        private readonly LifecycleTrackingComponent _child;
        public ChildHost(LifecycleTrackingComponent child) => _child = child;

        public override Component Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c);
            return c;
        }
    }
}
