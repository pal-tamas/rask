// rask-rewrite: keep the factory — this file holds BOTH surfaces on purpose and asserts they agree.
// Converting the factory half would leave a test comparing a chain to itself: still green, proving
// nothing. tools/RaskBuilderRewrite skips any file carrying this marker.

using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests;

// PROTOTYPE — the builder surface must be equivalent to the factory in LIFECYCLE and CACHE behaviour,
// not just in the HTML it produces.
//
// A generated factory does three things: GetOrCreate, assign the props, then NotifyParameters — it
// knows where the assignments end. An entry can only do the first; its props arrive afterwards, one
// setter at a time, and `Div.Class("a").Id("b")` has no natural end. So the entries defer the
// notification to the moment the parent's Render() returns, which is the first point at which the
// chain is provably complete. These tests pin what that has to be worth: the same OnMount /
// OnPropsChanged as the factory, the same Live.PropsDirty (so the render cache cannot serve a stale
// subtree), AND the same silence when nothing actually changed.
//
// The probes are STATEFUL (non-Element) components on purpose. An Element is never reached through
// RenderForLive at all — the serializer has a separate branch for it — so it can never be served from
// the render cache, which is exactly what masked this while every migrated call site was a tag.
internal sealed partial class LifecycleLeaf : Component
{
    public string? Word { get; set; }

    public Action? OnPing { get; set; }

    internal int Mounts;
    internal int PropsChanges;
    internal int Renders;

    protected override void OnMount() => Mounts++;

    protected override void OnPropsChanged() => PropsChanges++;

    protected override Component? Render()
    {
        Renders++;
        return Span[Word ?? ""];
    }
}

internal sealed partial class BuilderLifecycleHost : Component
{
    internal string Seed = "a";
    internal LifecycleLeaf? Leaf;

    protected override Component? Render() => Div[Leaf = LifecycleLeaf.Word(Seed).OnPing(() => { })];
}

internal sealed partial class FactoryLifecycleHost : Component
{
    internal string Seed = "a";
    internal LifecycleLeaf? Leaf;

    protected override Component? Render() =>
        Div()[Leaf = Rask.Core.Tests.Generated.LifecycleLeaf(Word: Seed, OnPing: () => { })];
}

public class BuilderLifecycleTests
{
    // One live render of `host`, driven the way a parent whose own props moved would drive it, so the
    // host re-executes Render() every time and the child is genuinely rebuilt through its surface.
    private static string Render(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        ctx.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    [Fact]
    public void An_entry_built_stateful_component_gets_the_factory_lifecycle()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new BuilderLifecycleHost();
        var factory = new FactoryLifecycleHost();

        Render(builder, sp);
        Render(factory, sp);
        builder.Seed = "b";
        factory.Seed = "b";
        Render(builder, sp);
        Render(factory, sp);

        Assert.Equal(factory.Leaf!.Mounts, builder.Leaf!.Mounts);
        Assert.Equal(factory.Leaf.PropsChanges, builder.Leaf.PropsChanges);
        Assert.Equal(1, builder.Leaf.Mounts);
        Assert.Equal(2, builder.Leaf.PropsChanges);
    }

    // The other half of the fold: an unchanged prop must stay silent, or the entry surface would dirty
    // its children every frame and the render cache would never hit. A lambda handed to a callback
    // setter is a fresh closure every render and must not count as a change (the factory excludes
    // auto-wrapped delegates from its diff for exactly this reason).
    [Fact]
    public void Unchanged_props_leave_the_entry_built_component_clean()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new BuilderLifecycleHost();
        var factory = new FactoryLifecycleHost();

        for (var i = 0; i < 3; i++)
        {
            Render(builder, sp);
            Render(factory, sp);
        }

        Assert.Equal(factory.Leaf!.PropsChanges, builder.Leaf!.PropsChanges);
        Assert.Equal(factory.Leaf.Renders, builder.Leaf.Renders);
        Assert.Equal(1, builder.Leaf.PropsChanges);
        Assert.Equal(1, builder.Leaf.Renders);
    }

    [Fact]
    public void An_entry_built_stateful_component_is_not_served_a_stale_cached_render()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new BuilderLifecycleHost();
        var factory = new FactoryLifecycleHost();

        Assert.Equal(Render(factory, sp), Render(builder, sp));
        builder.Seed = "b";
        factory.Seed = "b";

        var expected = Render(factory, sp);
        Assert.Equal("<div><span>b</span></div>", expected);
        Assert.Equal(expected, Render(builder, sp));
    }

    // Key is a reconciliation identity, not a reactive prop — the factory keeps it out of the diff so a
    // re-keyed component mounts fresh instead of re-rendering the old one. The setter must agree.
    [Fact]
    public void The_Key_setter_does_not_report_a_prop_change()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new KeyedLifecycleHost();

        Render(host, sp);
        host.Seed = 2;
        Render(host, sp);

        Assert.Equal(1, host.Leaf!.PropsChanges);
    }
}

internal sealed partial class KeyedLifecycleHost : Component
{
    internal int Seed = 1;
    internal LifecycleLeaf? Leaf;

    protected override Component? Render() => Div[Leaf = LifecycleLeaf.Key(Seed).Word("fixed")];
}
