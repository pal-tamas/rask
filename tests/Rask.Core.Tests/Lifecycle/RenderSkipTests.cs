using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public partial class RenderSkipTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_TwiceWithUnchangedProps_OnlyRunsOnce()
    {
        var sp = RenderHarness.EmptyServices();
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
        var sp = RenderHarness.EmptyServices();
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
        var sp = RenderHarness.EmptyServices();
        var c = new LifecycleTrackingComponent();

        using (RenderHarness.Render(c, sp, false))
        {
            c.ToHtml();
        }

        Assert.Equal(1, c.RenderCount);

        using (RenderHarness.Render(c, sp))
        {
            c.ToHtml();
        }

        Assert.Equal(2, c.RenderCount);
    }

    [Fact]
    public void SkippedParent_KeepsDescendantsAlive_AcrossRenders()
    {
        var sp = RenderHarness.EmptyServices();
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
        var sp = RenderHarness.EmptyServices();
        var root = new LifecycleTrackingComponent();

        root.RenderAsLiveRoot(sp);
        var afterFirst = root.RenderCount;

        // RenderAsLiveRoot itself forces a root render every call (the explicit "render now"
        // entry), so the second invocation re-runs Render even without calling
        // StateHasChanged() — matches the hot-reload + WS reconnect contract.
        root.RenderAsLiveRoot(sp);
        Assert.True(root.RenderCount > afterFirst, "root must re-render on direct RenderAsLiveRoot");
    }

    [Fact]
    public async Task StateHasChanged_WhileTheComponentIsRendering_IsNotLost()
    {
        // A lifecycle continuation changes state and calls StateHasChanged on a pool thread, and nothing stops
        // that landing while another thread's render of the same component is between reading the state and
        // finishing. The render used to clear StateDirty at its END, wiping that request: the stale output was
        // cached, and every later render replayed it. HttpPageTests timed out on exactly this (#1067); on the
        // Server host it is a page that stops updating.
        var sp = RenderHarness.EmptyServices();
        using var reading = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var child = new PausingComponent(reading, resume);
        var host = new StaticChildHost(child);

        host.RenderAsLiveRoot(sp);

        child.StateHasChanged();
        child.PauseNextRender();
        var walk = Task.Run(() => host.RenderAsLiveRoot(sp));
        Assert.True(reading.Wait(TimeSpan.FromSeconds(10)), "the child's render never started");

        child.Show("new");
        child.StateHasChanged();
        resume.Set();
        await walk.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("new", host.RenderAsLiveRoot(sp), StringComparison.Ordinal);
    }

    private sealed class StaticChildHost : Component
    {
        private readonly Component _child;
        public StaticChildHost(Component child) => _child = child;

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, false);
            return c;
        }
    }

    // Reads its state, then — once, on request — pauses inside Render() until the test lets it finish, so another
    // thread can change that state mid-render at a point the test controls rather than one a race might hit.
    private sealed class PausingComponent(ManualResetEventSlim reading, ManualResetEventSlim resume) : Component
    {
        private volatile string _shown = "old";
        private volatile bool _pauseNext;

        public void Show(string text) => _shown = text;

        public void PauseNextRender() => _pauseNext = true;

        protected override Component? Render()
        {
            var shown = _shown;
            if (_pauseNext)
            {
                _pauseNext = false;
                reading.Set();
                resume.Wait(TimeSpan.FromSeconds(10));
            }

            return Span[shown];
        }
    }

    private sealed class PassThroughChildHost : Component
    {
        private readonly Component _child;
        public PassThroughChildHost(Component child) => _child = child;

        protected override Component? Render()
        {
            var ctx = LiveRenderContext.Current!;
            var c = ctx.GetOrCreate(_ => _child);
            ctx.NotifyParameters(c, false);
            return Span[c];
        }
    }
}
