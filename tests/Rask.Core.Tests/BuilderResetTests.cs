// rask-rewrite: keep the factory — this file holds BOTH surfaces on purpose and asserts they agree.
// Converting the factory half would leave a test comparing a chain to itself: still green, proving
// nothing. tools/RaskBuilderRewrite skips any file carrying this marker.

using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests;

// PROTOTYPE — a builder entry must leave its component in the state the FACTORY would have left it,
// including for the props the call site did NOT mention.
//
// A generated factory assigns every parameter each render, so `Div(Id: "x")` on one render and `Div()`
// on the next puts Id back to null. A setter chain writes only what it names and the entry hands back
// the same instance, so without a reset the id survives — silently wrong HTML at every conditional call
// site, not merely a missed callback. The reset is split in two (see BuilderRuntime): the non-folding
// props are defaulted when the entry is created, the folding ones only at the end of the parent's
// Render(), so `Track` still compares against last render's value rather than a freshly blanked one.
internal sealed partial class ResetLeaf : Component
{
    public string? Word { get; set; }

    // Constant member initializer: an OPTIONAL factory param whose default is the initializer, not
    // null — so the reset has to restore "n/a", and blanking it would be a different kind of wrong.
    public string Note { get; set; } = "n/a";

    public int Count { get; set; } = 7;

    public Action? OnPing { get; set; }

    internal int PropsChanges;

    protected override void OnPropsChanged() => PropsChanges++;

    protected override Component? Render() =>
        Span[$"{Word ?? "-"}|{Note}|{Count}|{(OnPing is null ? "off" : "on")}"];
}

internal sealed partial class ResetBuilderHost : Component
{
    internal bool Full = true;
    internal ResetLeaf? Leaf;

    protected override Component? Render() =>
        Div[Leaf = Full ? ResetLeaf.Word("w").Note("L").Count(3).Ping(() => { }) : ResetLeaf];
}

internal sealed partial class ResetFactoryHost : Component
{
    internal bool Full = true;
    internal ResetLeaf? Leaf;

    protected override Component? Render() =>
        Div()[
            Leaf = Full
                ? Generated.ResetLeaf(Word: "w", Note: "L", Count: 3, OnPing: () => { })
                : Generated.ResetLeaf()
        ];
}

// The element half: attributes AND the DOM-event surface, which is where the omitted-prop bug is
// loudest — every conditional `Div.Class(...)` call site in a real app hits it.
internal sealed partial class ResetElementBuilderHost : Component
{
    internal bool Full = true;

    protected override Component? Render() =>
        Full ? Div.Id("x").Class("c").Title("t").OnClick(() => { }) : Div;
}

internal sealed partial class ResetElementFactoryHost : Component
{
    internal bool Full = true;

    protected override Component? Render() =>
        Full ? Div(Id: "x", Class: "c", Title: "t", OnClick: () => { }) : Div();
}

// The shape the Shop migration pilot broke on, reduced to its bones: a property whose SETTER DERIVES
// its value rather than storing what it was handed. `Router.Routes` is the real one — assigning null
// resolves `RouteRegistry.BuildTree()` and flattens the route leaves, so the factory passing
// `Routes: null` every render is what builds the routing table at all.
//
// For a prop like this, "already reads as the default" and "the setter has run" are different
// statements, and the reset used to conflate them: it skipped the write whenever the value already
// equalled the default, which on a never-assigned prop is always. `App.Render() => Router` — the shape
// at the root of every Rask app — rendered an empty <body>, with nothing to report it, because a
// nullable prop is not a required one and RASK038 has no claim on it.
internal sealed partial class DerivedSetterLeaf : Component
{
    private string? _resolved;

    public string? Origin
    {
        get => _resolved;
        set => _resolved = value ?? "built";
    }

    protected override Component? Render() => Span[_resolved ?? "nothing was built"];
}

internal sealed partial class DerivedSetterBuilderHost : Component
{
    protected override Component? Render() => Div[DerivedSetterLeaf];
}

internal sealed partial class DerivedSetterFactoryHost : Component
{
    protected override Component? Render() => Div()[Generated.DerivedSetterLeaf()];
}

// Both surfaces in one tree: a factory-built component must not be touched by the entry machinery —
// it re-assigns every parameter itself, and a stray reset would fight it.
internal sealed partial class ResetMixedHost : Component
{
    internal bool Full = true;

    protected override Component? Render() =>
        Div[
            Full ? Span.Id("keep") : Span.Id("keep"),
            Full ? Rask.Core.Components.Generated.P(Id: "factory") : Rask.Core.Components.Generated.P()
        ];
}

public class BuilderResetTests
{
    // One live render, driven the way a parent whose own props moved would drive it, so the host
    // re-executes Render() every time and the child is genuinely rebuilt through its surface.
    private static string Render(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        ctx.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    [Fact]
    public void A_prop_the_second_render_omits_is_gone_from_the_output()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new ResetElementBuilderHost();
        var factory = new ResetElementFactoryHost();

        Assert.Equal(Render(factory, sp), Render(builder, sp));
        builder.Full = false;
        factory.Full = false;

        var expected = Render(factory, sp);
        Assert.Equal("<div></div>", expected);
        Assert.Equal(expected, Render(builder, sp));
    }

    [Fact]
    public void An_omitted_prop_with_a_member_initializer_returns_to_the_initializer_not_null()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new ResetBuilderHost();
        var factory = new ResetFactoryHost();

        Assert.Equal(Render(factory, sp), Render(builder, sp));
        builder.Full = false;
        factory.Full = false;

        var expected = Render(factory, sp);
        Assert.Equal("<div><span>-|n/a|7|off</span></div>", expected);
        Assert.Equal(expected, Render(builder, sp));
    }

    // The regression the migration pilot found, at the size it can be reasoned about. A bare entry has
    // to leave a derived setter having RUN, which means the reset assigns unconditionally rather than
    // only when the value differs from the literal default.
    [Fact]
    public void A_setter_that_derives_its_value_runs_even_when_the_chain_never_names_it()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new DerivedSetterBuilderHost();
        var factory = new DerivedSetterFactoryHost();

        var expected = Render(factory, sp);
        Assert.Equal("<div><span>built</span></div>", expected);
        Assert.Equal(expected, Render(builder, sp));

        // And again: the second render is the one where the value is already there, so it is where an
        // unconditional write has to stay a write and must not start reporting a prop change for it.
        // (The FACTORY does report one here — it folds last render's derived value against the null it
        // passes, which never compare equal — so this is the one place the entry is deliberately better
        // than what it replaces rather than identical to it. `Router()` has been marking itself
        // prop-changed on every render since it was written; it is invisible only because Router opts
        // out of the render cache.)
        Assert.Equal(expected, Render(factory, sp));
        Assert.Equal(expected, Render(builder, sp));
    }

    // Dropping a prop IS a prop change — the entry-built child must be marked dirty for it, or the
    // render cache would serve the subtree it produced while the prop was still set.
    [Fact]
    public void Dropping_a_prop_reports_the_same_prop_change_the_factory_does()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new ResetBuilderHost();
        var factory = new ResetFactoryHost();

        Render(builder, sp);
        Render(factory, sp);
        builder.Full = false;
        factory.Full = false;
        Render(builder, sp);
        Render(factory, sp);

        Assert.Equal(factory.Leaf!.PropsChanges, builder.Leaf!.PropsChanges);
        Assert.Equal(2, builder.Leaf.PropsChanges);
    }

    // The other half of the fold, and the reason the folding props are reset at the END of the render
    // rather than when the entry is created: blanking Word before `.Word("w")` runs would make Track
    // compare "w" against null every frame, so a component whose props never move would look dirty on
    // every render and never hit the cache.
    [Fact]
    public void Re_supplying_the_same_props_still_reports_no_change()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new ResetBuilderHost();
        var factory = new ResetFactoryHost();

        for (var i = 0; i < 3; i++)
        {
            Render(builder, sp);
            Render(factory, sp);
        }

        Assert.Equal(factory.Leaf!.PropsChanges, builder.Leaf!.PropsChanges);
        Assert.Equal(1, builder.Leaf.PropsChanges);
    }

    [Fact]
    public void A_factory_built_sibling_keeps_its_own_props()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new ResetMixedHost();

        Assert.Equal("<div><span id=\"keep\"></span><p id=\"factory\"></p></div>", Render(host, sp));
        host.Full = false;
        Assert.Equal("<div><span id=\"keep\"></span><p></p></div>", Render(host, sp));
    }

    // A nested ToHtml() mid-Render must not strand either of the surrounding entries' pending resets —
    // the drain is keyed on which component BUILT the entry, not on where its slot landed.
    [Fact]
    public void A_nested_render_inside_the_chain_does_not_strand_a_pending_reset()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new NestedRenderResetHost();

        Assert.Contains("<span id=\"first\">", Render(host, sp), StringComparison.Ordinal);
        host.Full = false;

        var html = Render(host, sp);
        Assert.DoesNotContain("id=\"first\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"last\"", html, StringComparison.Ordinal);
    }

    // The half of the required-prop problem that no call-site analyzer reaches. RASK038 says the value
    // is ABSENT; this says last render's must not survive in its place. Withholding the entry used to
    // cover both at once, so relaxing that without this would have left `RequiredResetLeaf.Word("w")`
    // on one render and a bare `RequiredResetLeaf` on the next still rendering "w" — silently, and
    // forever, because the entry hands back the same instance.
    [Fact]
    public void A_required_prop_the_second_render_omits_goes_back_to_its_constructed_state()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new RequiredResetHost();

        Assert.Equal("<div><span>w|3</span></div>", Render(host, sp));
        host.Full = false;

        Assert.Equal("<div><span>-|0</span></div>", Render(host, sp));
        Assert.Null(host.Leaf!.Word);
        Assert.Equal(0, host.Leaf.Count);
    }

    // …and it must not cost the fold: a required prop re-supplied unchanged is still no change, the
    // same way an optional one is. Blanking it eagerly would have made Track compare "w" against null
    // every frame and no entry-built component with a required prop would ever hit the render cache.
    [Fact]
    public void Re_supplying_the_same_required_prop_still_reports_no_change()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new RequiredResetHost();

        for (var i = 0; i < 3; i++)
        {
            Render(host, sp);
        }

        Assert.Equal("<div><span>w|3</span></div>", Render(host, sp));
    }

    // Key is a reconciliation identity rather than a reactive prop, so it is reset with the non-folding
    // half — but it still has to be reset, because the factory assigns it every render too.
    [Fact]
    public void An_omitted_Key_is_cleared_like_every_other_prop()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new KeyedResetHost();

        Render(host, sp);
        Assert.Equal(7, host.Leaf!.Key);
        host.Keyed = false;
        Render(host, sp);
        Assert.Null(host.Leaf.Key);
    }
}

internal sealed partial class KeyedResetHost : Component
{
    internal bool Keyed = true;
    internal ResetLeaf? Leaf;

    protected override Component? Render() => Div[Leaf = Keyed ? ResetLeaf.Key(7) : ResetLeaf];
}

// A RASK001-required prop: non-nullable, no member initializer. The generated factory makes it a
// required ARGUMENT and re-applies it every render, so it can never go stale there — which is why the
// generator used to withhold the entry from any component that had one, and why nothing had to reset
// it. Now that those components DO get an entry, the reset is what stands in for the argument: a chain
// that stops naming the prop has nothing to re-apply, and the entry hands back the same instance.
//
// RASK038 reports the omission at the call site, but only for a chain it can read end to end. This is
// the shape it explicitly cannot (RASK039) — and the whole reason the two halves are separate.
#pragma warning disable CS8618 // the point of the test is a non-nullable prop with no initializer
internal sealed partial class RequiredResetLeaf : Component
{
    public string Word { get; set; }

    public int Count { get; set; }

    protected override Component? Render() => Span[$"{Word ?? "-"}|{Count}"];
}
#pragma warning restore CS8618

internal sealed partial class RequiredResetHost : Component
{
    internal bool Full = true;
    internal RequiredResetLeaf? Leaf;

    protected override Component? Render()
    {
#pragma warning disable RASK039 // deliberately the split chain the analyzer cannot answer
        var leaf = RequiredResetLeaf;
#pragma warning restore RASK039
        return Div[Leaf = Full ? leaf.Word("w").Count(3) : leaf];
    }
}

// A Render() that serializes another component in the MIDDLE of building its own tree — the shape
// behind issue #627, and the one thing that could interleave two components' pending slots. The reset
// is owner-keyed rather than position-keyed precisely so this cannot strand a slot: a stranded slot is
// both a prop that never gets reset and a leak on a stack only its owner pops.
internal sealed partial class NestedRenderResetHost : Component
{
    internal bool Full = true;

    protected override Component? Render() =>
        Div[
            Full ? Span.Id("first") : Span,
            Raw(new NestedRenderInner().ToHtml()),
            Full ? Em.Id("last") : Em
        ];
}

internal sealed partial class NestedRenderInner : Component
{
    protected override Component? Render() => B.Id("inner");
}
