using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class ReconciliationTests
{
    private static readonly IServiceProvider EmptyServices =
        RenderHarness.EmptyServices();

    [Fact]
    public void GetOrCreate_FreshContext_AllocatesAndStores()
    {
        var root = new StubComponent(Span());
        var factoryCalls = 0;
        using var ctx = LiveRenderContext.Begin(root, EmptyServices);

        var c1 = ctx.GetOrCreate<CounterStub>(_ =>
        {
            factoryCalls++;
            return new CounterStub();
        });

        Assert.Equal(1, factoryCalls);
        Assert.NotNull(c1);
    }

    [Fact]
    public void GetOrCreate_ReusesPreviousInstance_AtSamePosition()
    {
        var root = new StubComponent(Span());
        var prev = new CounterStub { Value = 7 };
        var previousChildren = new Dictionary<(Type, int), Component> { [(typeof(CounterStub), 0)] = prev };

        using var ctx = LiveRenderContextFactoryAccess.Begin(root, previousChildren);
        var factoryCalls = 0;

        var resolved = ctx.GetOrCreate<CounterStub>(_ =>
        {
            factoryCalls++;
            return new CounterStub();
        });

        Assert.Same(prev, resolved);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(7, resolved.Value);
    }

    [Fact]
    public void GetOrCreate_TypeMismatch_AllocatesFresh()
    {
        var root = new StubComponent(Span());
        var prev = new OtherStub();
        var previousChildren = new Dictionary<(Type, int), Component> { [(typeof(CounterStub), 0)] = prev };

        using var ctx = LiveRenderContextFactoryAccess.Begin(root, previousChildren);
        var resolved = ctx.GetOrCreate<CounterStub>(_ => new CounterStub());

        Assert.IsType<CounterStub>(resolved);
        Assert.NotSame(prev, resolved);
    }

    [Fact]
    public void GetOrCreate_SequentialPositions_GetDistinctKeys()
    {
        var root = new StubComponent(Span());
        var p0 = new CounterStub { Value = 1 };
        var p1 = new CounterStub { Value = 2 };
        var previousChildren = new Dictionary<(Type, int), Component>
        {
            [(typeof(CounterStub), 0)] = p0,
            [(typeof(CounterStub), 1)] = p1
        };

        using var ctx = LiveRenderContextFactoryAccess.Begin(root, previousChildren);

        var first = ctx.GetOrCreate<CounterStub>(_ => new CounterStub());
        var second = ctx.GetOrCreate<CounterStub>(_ => new CounterStub());

        Assert.Same(p0, first);
        Assert.Same(p1, second);
    }

    [Fact]
    public void RenderAsLiveRoot_DropsUnreferencedChildren()
    {
        // First render produces two Counter children; second produces one.
        // The dropped one must not appear in PersistedChildren after the second render.
        var renderCalls = 0;
        var view = new StubComponent(() =>
        {
            renderCalls++;
            var ctx = LiveRenderContext.Current!;
            if (renderCalls == 1)
            {
                ctx.GetOrCreate<CounterStub>(_ => new CounterStub { Value = 100 });
                ctx.GetOrCreate<CounterStub>(_ => new CounterStub { Value = 200 });
            }
            else
            {
                ctx.GetOrCreate<CounterStub>(_ => new CounterStub { Value = 999 });
            }

            return new Span();
        });

        view.RenderAsLiveRoot(EmptyServices);
        Assert.Equal(2, view.PersistedChildren.Count);

        view.RenderAsLiveRoot(EmptyServices);
        Assert.Single(view.PersistedChildren);
        // The first-position Counter survived (Value=100); second-position dropped.
        Assert.True(view.PersistedChildren.ContainsKey((typeof(CounterStub), 0)));
        Assert.False(view.PersistedChildren.ContainsKey((typeof(CounterStub), 1)));
    }

    private sealed class CounterStub : Component
    {
        public int Value;
        protected override RenderResult Render() => Raw($"<x>{Value}</x>");
    }

    private sealed class OtherStub : Component
    {
        protected override RenderResult Render() => Raw("<y/>");
    }
}

internal static class LiveRenderContextFactoryAccess
{
    private static readonly IServiceProvider EmptyServices =
        RenderHarness.EmptyServices();

    // Mirrors the internal Begin overload for tests. Internals are visible to the test project.
    // After the per-parent refactor, "previous children" lives on the parent component itself,
    // so we seed the root's previous-children dict before opening the context.
    public static LiveRenderContext Begin(Component root, Dictionary<(Type, int), Component> previousChildren)
    {
        root.SeedPreviousChildren(previousChildren);
        return LiveRenderContext.Begin(root, EmptyServices);
    }
}
