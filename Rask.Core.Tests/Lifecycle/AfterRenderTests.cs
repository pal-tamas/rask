using Rask.Core.Live;
using Rask.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Tests.Lifecycle;

public class AfterRenderTests
{
    [Fact]
    public void OnAfterRender_FiresFirstTrue_ThenFalse()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var root = new ChildHostingRoot(new LifecycleTrackingComponent());
        var session = new LiveSession("test", root, scope);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(new[] { true, false, false }, root.Child.AfterRenderFlags);
        Assert.Equal(3, root.Child.AfterRenderCount);
    }

    [Fact]
    public void OnAfterRender_FiresOnRoot()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var root = new LifecycleTrackingComponent();
        var session = new LiveSession("test", root, scope);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(new[] { true, false }, root.AfterRenderFlags);
    }

    private sealed class ChildHostingRoot : Component
    {
        public ChildHostingRoot(LifecycleTrackingComponent child) => Child = child;
        public LifecycleTrackingComponent Child { get; }

        public override Component Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => Child);
            ctx.NotifyParameters(c);
            return c;
        }
    }
}
