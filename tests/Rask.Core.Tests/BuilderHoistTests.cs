using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests;

// A factory evaluates its arguments BEFORE it constructs the component; a setter chain constructs the
// receiver first and evaluates the argument after. So the same markup written two ways builds a
// component-valued argument on opposite sides of its own parent:
//
//     SlotHost(Payload: LifecycleLeaf(Word: "x"))   // leaf, then host
//     SlotHost.Payload(LifecycleLeaf.Word("x"))     // host, then leaf
//
// Identity in GetOrCreateChild is positional — a single counter per parent, keyed (Type, position) —
// so those two orders hand the same two children different positions. That is the whole of the "does
// the rewriter have to hoist a component-valued argument into a local" question, and nothing pinned
// it. These tests answer it.
//
// The verdict they encode: positional identity only has to be STABLE render-to-render, never equal to
// the numbering some other spelling of the same tree would have produced. The rewrite renumbers once,
// at the edit, exactly as any other source edit does — so no hoisting is needed. What it does NOT
// survive is one Render() body that emits the same subtree through the factory on one render and
// through a chain on the next, which the last test pins as the one shape a partial rewrite must not
// produce.

// A component with a Component-typed slot, so an argument can itself be a component.
internal sealed partial class SlotHost : Component
{
    public Component? Payload { get; set; }

    protected override string? TagName => null;

    protected override Component? Render() => Div[Payload];
}

// Fully migrated: receiver first, argument second, every render.
internal sealed partial class ChainOrderHost : Component
{
    internal LifecycleLeaf? Leaf;
    internal SlotHost? Outer;

    protected override Component? Render() =>
        Div[Outer = SlotHost.Payload(Leaf = LifecycleLeaf.Word("x"))];
}

// The same tree before the rewrite: argument first, receiver second, every render.
internal sealed partial class FactoryOrderHost : Component
{
    internal LifecycleLeaf? Leaf;
    internal SlotHost? Outer;

    protected override Component? Render() =>
        Div()[Outer = Generated.SlotHost(Payload: Leaf = Generated.LifecycleLeaf(Word: "x"))];
}

// A half-rewritten tree, which is what a project-by-project migration leaves behind at any moment: a
// factory whose argument is already a chain.
internal sealed partial class FactoryOuterChainPayloadHost : Component
{
    internal LifecycleLeaf? Leaf;

    protected override Component? Render() =>
        Div()[Generated.SlotHost(Payload: Leaf = LifecycleLeaf.Word("x"))];
}

// And the other half-rewritten shape: a chain whose argument is still a factory call.
internal sealed partial class ChainOuterFactoryPayloadHost : Component
{
    internal LifecycleLeaf? Leaf;

    protected override Component? Render() =>
        Div[SlotHost.Payload(Leaf = Generated.LifecycleLeaf(Word: "x"))];
}

// The shape a partial rewrite must never leave: two branches of ONE Render() that build the same two
// component types in opposite orders, because only one of them was converted.
internal sealed partial class SwitchingOrderHost : Component
{
    internal LifecycleLeaf? Leaf;
    internal bool UseChain;

    protected override Component? Render() =>
        UseChain
            ? Div[SlotHost.Payload(Leaf = LifecycleLeaf.Word("x"))]
            : Div()[Generated.SlotHost(Payload: Leaf = Generated.LifecycleLeaf(Word: "x"))];
}

public class BuilderHoistTests
{
    // Same driver as BuilderLifecycleTests: one live render, forced to re-execute Render() so the
    // children are genuinely rebuilt through their surface each time.
    private static string Render(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        ctx.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    // The hypothesis, stated as a test: with the whole tree on one surface, the receiver-then-argument
    // order repeats identically every render, so every child keeps its instance and mounts once. This
    // is what makes hoisting unnecessary.
    [Fact]
    public void A_chain_keeps_every_child_instance_across_renders()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new ChainOrderHost();

        Render(host, sp);
        var leaf = host.Leaf!;
        var outer = host.Outer!;
        Render(host, sp);
        Render(host, sp);

        Assert.Same(leaf, host.Leaf);
        Assert.Same(outer, host.Outer);
        Assert.Equal(1, leaf.Mounts);
    }

    // ...and it is worth exactly what the factory's order was worth: same HTML, same lifecycle.
    [Fact]
    public void The_two_orders_are_equivalent_render_to_render()
    {
        var sp = RenderHarness.EmptyServices();
        var chain = new ChainOrderHost();
        var factory = new FactoryOrderHost();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(Render(factory, sp), Render(chain, sp));
        }

        Assert.Equal(factory.Leaf!.Mounts, chain.Leaf!.Mounts);
        Assert.Equal(factory.Leaf.PropsChanges, chain.Leaf.PropsChanges);
        Assert.Equal(factory.Leaf.Renders, chain.Leaf.Renders);
    }

    // A tree caught mid-migration is stable too, in both nesting directions — which is what lets the
    // rewrite land one project (or one file) at a time.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_half_rewritten_tree_is_stable_across_renders(bool factoryOutside)
    {
        var sp = RenderHarness.EmptyServices();
        Component host = factoryOutside
            ? new FactoryOuterChainPayloadHost()
            : new ChainOuterFactoryPayloadHost();

        var first = Render(host, sp);
        var leaf = Leaf(host);
        Render(host, sp);
        var third = Render(host, sp);

        Assert.Equal(first, third);
        Assert.Same(leaf, Leaf(host));
        Assert.Equal(1, leaf.Mounts);

        static LifecycleLeaf Leaf(Component host) => host switch
        {
            FactoryOuterChainPayloadHost h => h.Leaf!,
            ChainOuterFactoryPayloadHost h => h.Leaf!,
            _ => throw new InvalidOperationException(),
        };
    }

    // The one shape that does break, pinned so the rewriter's rule has a reason: swap the surface a
    // Render() uses BETWEEN renders and the two children swap positions, so neither is found in the
    // previous frame and both are rebuilt — the leaf loses its instance and mounts a second time. No
    // fixed source can do this; a Render() whose branches were converted unevenly can. The rewriter
    // therefore converts a whole Render() body or none of it.
    [Fact]
    public void Switching_surface_between_renders_renumbers_the_children_and_loses_the_instance()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new SwitchingOrderHost();

        var before = Render(host, sp);
        var leaf = host.Leaf!;
        Assert.Equal(1, leaf.Mounts);

        host.UseChain = true;
        var after = Render(host, sp);

        // The markup is the same either way; only the identity underneath it moved.
        Assert.Equal(before, after);
        Assert.NotSame(leaf, host.Leaf);
        Assert.Equal(1, host.Leaf!.Mounts);
    }
}
