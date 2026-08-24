using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests;

// A setter chain constructs its receiver first and evaluates the argument after, so a component-valued
// argument is built AFTER its own parent:
//
//     SlotHost.Payload(LifecycleLeaf.Word("x"))     // host, then leaf
//
// Identity in GetOrCreateChild is positional — a single counter per parent, keyed (Type, position) —
// so that order is what numbers these two children. What matters is that the numbering is STABLE
// render to render: the same Render() body must hand the same child the same position every time, or
// it is not found in the previous frame and gets rebuilt. That is what this pins, and it is why a
// component-valued argument never has to be hoisted into a local.

// A component with a Component-typed slot, so an argument can itself be a component.
internal sealed partial class SlotHost : Component
{
    public Component? Payload { get; set; }

    protected override string? TagName => null;

    protected override Component? Render() => Div[Payload];
}

// Receiver first, argument second, every render.
internal sealed partial class ChainOrderHost : Component
{
    internal LifecycleLeaf? Leaf;
    internal SlotHost? Outer;

    protected override Component? Render() =>
        Div[Outer = SlotHost.Payload(Leaf = LifecycleLeaf.Word("x"))];
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

    // The receiver-then-argument order repeats identically every render, so every child keeps its
    // instance and mounts once. This is what makes hoisting unnecessary.
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
}
