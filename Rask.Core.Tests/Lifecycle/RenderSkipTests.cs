using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class RenderSkipTests
{
    [Fact]
    public void Render_TwiceWithUnchangedProps_OnlyRunsOnce()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var child = new LifecycleTrackingComponent();
        var host = new StaticChildHost(child);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);

        // First render runs the child's Render(); second render skips it because the child
        // has no prop change, no StateHasChanged call, and is not opted out of caching.
        Assert.Equal(1, child.RenderCount);
    }

    [Fact]
    public void Render_StateHasChangedOnChild_RerendersOnlyThatChild()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var child = new LifecycleTrackingComponent();
        var host = new StaticChildHost(child);

        host.RenderAsLiveRoot(sp);
        Assert.Equal(1, child.RenderCount);

        child.StateHasChanged();
        host.RenderAsLiveRoot(sp);

        // The child marked itself dirty, so its Render() runs again on the next pass even
        // though the host re-emits the same tree.
        Assert.Equal(2, child.RenderCount);
    }

    [Fact]
    public void Render_PropsChange_RerendersThatComponent()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, false);
            c.ToHtml();
        }

        Assert.Equal(1, c.RenderCount);

        using (var ctx = LiveRenderContext.Begin(c, sp))
        {
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, true);
            c.ToHtml();
        }

        Assert.Equal(2, c.RenderCount);
    }

    [Fact]
    public void SkippedParent_KeepsDescendantsAlive_AcrossRenders()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var grandchild = new LifecycleTrackingComponent();
        var middle = new PassThroughChildHost(grandchild);
        var host = new StaticChildHost(middle);

        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);
        host.RenderAsLiveRoot(sp);

        // Across three renders, the grandchild is never disposed even though `middle` skips
        // its render after the first pass — the dispose pass walks the alive tree via
        // _children, and skipped components still own their children.
        Assert.Equal(1, grandchild.RenderCount);

        grandchild.StateHasChanged();
        host.RenderAsLiveRoot(sp);

        // Even though every ancestor skipped, the dirty grandchild re-renders — the
        // serializer walks the cached parent trees down to it.
        Assert.Equal(2, grandchild.RenderCount);
    }

    [Fact]
    public void RootStateHasChanged_ForcesRootRender()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var root = new LifecycleTrackingComponent();

        root.RenderAsLiveRoot(sp);
        var afterFirst = root.RenderCount;

        // RenderAsLiveRoot itself forces a root render every call (the explicit "render now"
        // entry), so the second invocation re-runs Render even without calling
        // StateHasChanged() — matches the hot-reload + WS reconnect contract.
        root.RenderAsLiveRoot(sp);
        Assert.True(root.RenderCount > afterFirst, "root must re-render on direct RenderAsLiveRoot");
    }

    private sealed class StaticChildHost : Component
    {
        private readonly Component _child;
        public StaticChildHost(Component child) => _child = child;

        protected override Component Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, false);
            return c;
        }
    }

    private sealed class PassThroughChildHost : Component
    {
        private readonly Component _child;
        public PassThroughChildHost(Component child) => _child = child;

        protected override Component Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, false);
            return Span()[c];
        }
    }
}
