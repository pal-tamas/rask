using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class ReconciliationTests : global::Rask.Core.RaskMarkup
{
    private static readonly IServiceProvider EmptyServices =
        RenderHarness.EmptyServices();

    [Fact]
    public void A_fresh_context_allocates_and_stores_the_component()
    {
        var root = new StubComponent(Span);
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
    public void The_previous_instance_at_the_same_position_is_reused()
    {
        var root = new StubComponent(Span);
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
    public void A_type_mismatch_allocates_a_fresh_instance()
    {
        var root = new StubComponent(Span);
        var prev = new OtherStub();
        var previousChildren = new Dictionary<(Type, int), Component> { [(typeof(CounterStub), 0)] = prev };

        using var ctx = LiveRenderContextFactoryAccess.Begin(root, previousChildren);
        var resolved = ctx.GetOrCreate<CounterStub>(_ => new CounterStub());

        Assert.IsType<CounterStub>(resolved);
        Assert.NotSame(prev, resolved);
    }

    [Fact]
    public void Sequential_positions_get_distinct_keys()
    {
        var root = new StubComponent(Span);
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
    public void Rendering_as_the_live_root_drops_unreferenced_children()
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

    // Regression: a lazy IEnumerable<Component> (a `yield`/LINQ pipeline) passed to the children indexer
    // must be materialised RIGHT THEN, during Render — so any component factory inside it runs while the
    // owning component's child-reuse bookkeeping (GetOrCreateChild positions + PreviousChildren) is live.
    // Deferring evaluation to serialization would recreate embedded components every render and drop
    // their state (the bug that broke inline live demos co-mounted in a guide via a yield-built list).
    [Fact]
    public void A_lazy_enumerable_in_the_children_indexer_is_evaluated_immediately()
    {
        var evaluated = 0;

        IEnumerable<Component> Lazy()
        {
            foreach (var _ in Enumerable.Range(0, 3))
            {
                evaluated++;
                yield return Span;
            }
        }

        var div = Div[Lazy()];

        Assert.Equal(3, evaluated); // fully enumerated by the indexer, not deferred
        Assert.IsType<Component[]>(div.Children); // stored as a materialised array
    }

    [Fact]
    public void An_already_materialised_collection_in_the_children_indexer_passes_through_without_a_copy()
    {
        var list = new List<Component> { Span, Div };

        var div = Div[(IEnumerable<Component>)list];

        Assert.Same(list, div.Children); // no redundant copy for a ready collection
    }

    private sealed class CounterStub : Component
    {
        public int Value;
        protected override Component? Render() => Raw.Value($"<x>{Value}</x>");
    }

    private sealed class OtherStub : Component
    {
        protected override Component? Render() => Raw.Value("<y/>");
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
