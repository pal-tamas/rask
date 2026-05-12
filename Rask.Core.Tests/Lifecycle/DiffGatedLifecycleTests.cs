using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Tests.Lifecycle;

public class DiffGatedLifecycleTests
{
    [Fact]
    public void CachedChild_UnchangedProps_FiresOnParametersSetOnceOnFirstRenderOnly()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 3; i++)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, propsChanged: false);
        }

        Assert.Equal(1, c.ParametersSetCount);
        Assert.Equal(1, c.ParametersSetAsyncCount);
    }

    [Fact]
    public void CachedChild_ChangedProps_FiresOnParametersSetEachTime()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        for (var i = 0; i < 3; i++)
        {
            using var ctx = LiveRenderContext.Begin(c, sp);
            var resolved = ctx.GetOrCreate(_ => c);
            ctx.NotifyParameters(resolved, propsChanged: true);
        }

        Assert.Equal(3, c.ParametersSetCount);
        Assert.Equal(3, c.ParametersSetAsyncCount);
    }

    [Fact]
    public void FirstRender_FiresOnParametersSet_EvenWhenPropsChangedFlagIsFalse()
    {
        // A first-time render is always lifecycle-driven: OnInitialized + OnParametersSet must
        // fire regardless of the diff flag, because the component has never seen its initial values.
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = new LifecycleTrackingComponent();

        using var ctx = LiveRenderContext.Begin(c, sp);
        var resolved = ctx.GetOrCreate(_ => c);
        ctx.NotifyParameters(resolved, propsChanged: false);

        Assert.Equal(1, c.InitializedCount);
        Assert.Equal(1, c.ParametersSetCount);
    }

    [Fact]
    public void MixedRenders_OnParametersSetFiresOnlyOnChangeOrFirst()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
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

        Assert.Equal(3, c.ParametersSetCount);
    }
}
