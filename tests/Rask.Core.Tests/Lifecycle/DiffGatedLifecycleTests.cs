using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Lifecycle;

public class DiffGatedLifecycleTests
{
    [Fact]
    public void CachedChild_UnchangedProps_FiresOnPropsChangedOnceOnFirstRenderOnly()
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
    public void CachedChild_ChangedProps_FiresOnPropsChangedEachTime()
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
    public void FirstRender_FiresOnPropsChanged_EvenWhenPropsChangedFlagIsFalse()
    {
        // A first-time render is always lifecycle-driven: OnMount + OnPropsChanged must
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
    public void MixedRenders_OnPropsChangedFiresOnlyOnChangeOrFirst()
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
