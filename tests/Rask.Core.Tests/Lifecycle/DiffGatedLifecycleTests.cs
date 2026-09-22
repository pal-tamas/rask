using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class DiffGatedLifecycleTests
{
    [Fact]
    public void A_cached_child_with_unchanged_props_fires_OnPropsChanged_once_on_the_first_render_only()
    {
        var sp = RenderHarness.EmptyServices();
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
    public void A_cached_child_with_changed_props_fires_OnPropsChanged_each_time()
    {
        var sp = RenderHarness.EmptyServices();
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
    public void The_first_render_fires_OnPropsChanged_even_when_the_props_changed_flag_is_false()
    {
        // A first-time render is always lifecycle-driven: Mount + Updated must
        // fire regardless of the diff flag, because the component has never seen its initial values.
        var sp = RenderHarness.EmptyServices();
        var c = new LifecycleTrackingComponent();

        using var ctx = LiveRenderContext.Begin(c, sp);
        var resolved = ctx.GetOrCreate(_ => c);
        ctx.NotifyParameters(resolved, false);

        Assert.Equal(1, c.MountCount);
        Assert.Equal(1, c.PropsChangedCount);
    }

    [Fact]
    public void Across_mixed_renders_OnPropsChanged_fires_only_on_a_change_or_the_first()
    {
        var sp = RenderHarness.EmptyServices();
        var c = new LifecycleTrackingComponent();

        // r1 first render -> fires
        // r2 unchanged   -> skipped
        // r3 changed     -> fires
        // r4 unchanged   -> skipped
        // r5 changed     -> fires  => total 3 fires
        var sequence = new[] { false, false, true, false, true };
        foreach (var propsChanged in sequence)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, propsChanged);
        }

        Assert.Equal(3, c.PropsChangedCount);
    }
}
