using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class RenderedTests
{
    [Fact]
    public void OnRendered_fires_first_with_true_then_with_false()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var root = new ChildHostingRoot(new LifecycleTrackingComponent());
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(new[] { true, false, false }, root.Component.RenderedFlags);
        Assert.Equal(3, root.Component.RenderedCount);
    }

    [Fact]
    public void OnRendered_fires_on_the_root()
    {
        var sp = RenderHarness.EmptyServices();
        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var root = new LifecycleTrackingComponent();
        var session = new LiveSession("test", root, scope, LiveDiffMode.Auto);

        session.View.RenderAsLiveRoot(scope.ServiceProvider);
        session.View.RenderAsLiveRoot(scope.ServiceProvider);

        Assert.Equal(new[] { true, false }, root.RenderedFlags);
    }

    private sealed class ChildHostingRoot : Component
    {
        public ChildHostingRoot(LifecycleTrackingComponent child) => Component = child;
        public LifecycleTrackingComponent Component { get; }

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => Component);
            ctx.NotifyParameters(c, true);
            return c;
        }
    }
}
