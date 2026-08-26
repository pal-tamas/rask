using Rask.Core.Components;
using Rask.Core.HeadAssets;
using Rask.Core.Live;
using Rask.Html.Components;

#pragma warning disable RASK014 // the tests need the very instance they hand to the render context

namespace Rask.Core.Tests;

// The three places a builder entry can be built that are NOT "somewhere inside a Render() that returns
// normally". An entry's props and lifecycle are completed by the parent when its Render() returns, so
// each of these is a separate promise to keep.
//
//   1. a Render() that THROWS — a supported path (ErrorBoundary re-renders a fallback), and the entries
//      it built before the throw still have to be swept off the per-thread slot stack;
//   2. an entry inside a Head override, which the serializer collects outside the component's own render;
//   3. an entry built from a lifecycle hook, which the deferred commit itself is running.
[global::Rask.Core.RaskMarkup]
internal sealed partial class ThrowingRenderHost : Component
{
    internal bool Fail;

    protected override Component? Render()
    {
        // The entry — and its pending reset — is created BEFORE the throw, which is the whole point.
        var div = Div;
        if (Fail)
        {
            throw new InvalidOperationException("render failed");
        }

        return div.Id("x");
    }
}

// A Head override that drops a prop on the second frame — the head's equivalent of the conditional call
// site in BuilderResetTests. `content` has to be gone the moment the second branch renders, which can
// only happen if the pending reset is owned by the component whose Head this is.
[global::Rask.Core.RaskMarkup]
internal sealed partial class HeadEntryLeaf : Component
{
    public string? Word { get; set; }

    internal Meta? Probe;

    protected override Component? HeadAssets =>
        Probe = Word == "a" ? Meta.Name("probe").Content("keep").Value : Meta.Name("probe");

    protected override Component? Render() => Span[Word ?? ""];
}

[global::Rask.Core.RaskMarkup]
internal sealed partial class HeadEntryHost : Component
{
    internal string Seed = "a";
    internal HeadEntryLeaf? Leaf;

    protected override Component? Render() => Div[Leaf = HeadEntryLeaf.Word(Seed)];
}

// A child whose OnMount builds a component. Under the factory that hook fires from inside the parent's
// Render(); under the entries it fires from the parent's deferred commit, i.e. while the parent is
// walking its own child map — and building anything writes to that very map.
[global::Rask.Core.RaskMarkup]
internal sealed partial class MountBuildsLeaf : Component
{
    public string? Word { get; set; }

    internal Component? Built;

    protected override void OnMount() => Built = Span.Id("from-mount");

    protected override Component? Render() => Em[Word ?? ""];
}

[global::Rask.Core.RaskMarkup]
internal sealed partial class MountBuildsHost : Component
{
    internal MountBuildsLeaf? Leaf;

    protected override Component? Render() => Div[Leaf = MountBuildsLeaf.Word("a")];
}

public partial class BuilderRenderPathTests : global::Rask.Core.RaskMarkup
{
    // One live render, driven the way a parent whose own props moved would drive it, so the host
    // re-executes Render() every time and its children are genuinely rebuilt through their surface.
    private static string Render(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        ctx.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }

    // The slot stack is per-THREAD and shared by every session on it, and a slot leaves it only through
    // the drain at the end of the render that pushed it. A throw that skips the drain therefore both
    // pins a live subtree and — because the next successful render pushes a SECOND slot for the same
    // target — lets the stale slot's pending mask blank a prop the new chain has just set.
    [Fact]
    public void A_render_that_throws_does_not_strand_the_entries_it_built()
    {
        var sp = RenderHarness.EmptyServices();
        var host = new ThrowingRenderHost { Fail = true };

        Assert.Throws<InvalidOperationException>(() => Render(host, sp));

        host.Fail = false;
        Assert.Equal("<div id=\"x\"></div>", Render(host, sp));
    }

    // The head half of the omitted-prop reset. The serializer collects a Head contribution from outside
    // the component's own render scope, so an entry built there used to be owned by the ENCLOSING
    // component: its reset ran a frame late (or never, on a shell that renders once).
    [Fact]
    public void An_entry_inside_a_Head_override_drops_an_omitted_prop()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new HeadEntryHost();

        Render(builder, sp);
        Assert.Equal("keep", builder.Leaf!.Probe!.Content);

        builder.Seed = "b";
        Render(builder, sp);

        Assert.Null(builder.Leaf!.Probe!.Content);
    }

    // The head contribution still has to reach the page, in walk order, whether or not the component
    // re-rendered — it is re-collected every frame from a registry that is cleared every frame.
    [Fact]
    public void An_entry_built_Head_contribution_reaches_the_page()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new HeadEntryHost();

        using var ctx = LiveRenderContext.Begin(builder, sp);
        var resolved = ctx.GetOrCreate(_ => builder);
        ctx.NotifyParameters(resolved, propsChanged: true);

        Assert.Equal("<div><span>a</span></div>", resolved.ToHtml());
        Assert.Contains(
            "name=\"probe\" content=\"keep\"",
            ctx.HeadAssets.ApplyTo(HeadAssetRegistry.Sentinel, sp),
            StringComparison.Ordinal);
    }

    // A lifecycle hook is user code and may build components; the commit that runs it is walking the
    // parent's child map, and building anything writes to that map.
    [Fact]
    public void A_child_that_builds_from_OnMount_does_not_break_the_deferred_commit()
    {
        var sp = RenderHarness.EmptyServices();
        var builder = new MountBuildsHost();

        Assert.Equal("<div><em>a</em></div>", Render(builder, sp));
        Assert.NotNull(builder.Leaf!.Built);
        Assert.Equal("<div><em>a</em></div>", Render(builder, sp));
    }
}
