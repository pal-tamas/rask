// rask-rewrite: keep the factory — this file holds BOTH surfaces on purpose and asserts they agree.
// Converting the factory half would leave a test comparing a chain to itself: still green, proving
// nothing. tools/RaskBuilderRewrite skips any file carrying this marker.

using Rask.Core.Live;

#pragma warning disable RASK014 // the pin needs the very instance it hands to the render context

namespace Rask.Core.Tests.Performance;

// PROTOTYPE — the builder surface has to cost what the factory costs, per render, in bytes.
//
// The entry's omitted-prop reset (BuilderRuntime) is the hot-path addition: every entry pushes a slot
// carrying its pending mask and the per-type reset to run at the end of the parent's Render(). None of
// that may allocate in the steady state — the slot stack is a per-thread buffer reused across renders,
// and the reset routines are handed over as method groups, which the compiler caches in a static
// field. A per-render delegate or a fresh list would show up here immediately.
//
// Pinned as a RATIO against the identical tree written with the generated factory rather than as an
// absolute byte count: both surfaces share the whole live-render harness, so the comparison isolates
// what the surface itself costs and does not have to be re-tuned whenever the harness changes.
internal sealed partial class AllocEntryProbe : Component
{
    protected override Component? Render() =>
        Div.Id("counter").Class("counter")[
            Span.Class("value")["42"],
            Button.Class("inc")["+"]
        ];
}

internal sealed partial class AllocFactoryProbe : Component
{
    protected override Component? Render() =>
        Div(Id: "counter", Class: "counter")[
            Span(Class: "value")["42"],
            Button(Class: "inc")["+"]
        ];
}

// The generic form control's entry is a static METHOD, so its reset arguments are method groups of a
// GENERIC method. Those are cached per instantiation in a generic `<>O` holder — but only if the
// compiler can; a per-call delegate here would be a silent per-render allocation on every bound input
// in an app.
internal sealed partial class AllocBoundEntryProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    protected override Component? Render() => Div[Input.Bind(() => Model.Name).Id("name")];
}

internal sealed partial class AllocBoundFactoryProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    protected override Component? Render() =>
        Div()[Rask.Core.Components.Generated.Input(() => Model.Name, Id: "name")];
}

// The event surface: every one of Element's ~88 handler pairs is a
// plain `Action?` / `Action<TArgs>?` property over the same dictionary slot, so setting one and resetting
// one are both supposed to be free. Critically, an element handler must stay UNWRAPPED: an AutoCallback
// wrapper would be one closure per handler per render, which is exactly what this pins.
internal sealed partial class AllocEventEntryProbe : Component
{
    internal int Clicks;

    protected override Component? Render() =>
        Div.Id("counter")[
            Button.Class("inc").OnClick(Bump)["+"],
            Span.Class("value").OnMouseDown(_ => Bump())["42"]
        ];

    private void Bump() => Clicks++;
}

internal sealed partial class AllocEventFactoryProbe : Component
{
    internal int Clicks;

    protected override Component? Render() =>
        Div(Id: "counter")[
            Button(Class: "inc", OnClick: Bump)["+"],
            Span(Class: "value", OnMouseDown: _ => Bump())["42"]
        ];

    private void Bump() => Clicks++;
}

// The other side of the wrap rule. A NON-Element component's callback stays AutoCallback-wrapped, so
// it costs one closure per handler per render — on BOTH surfaces, which is the parity being pinned.
// Nothing may be added on top of that one closure.
internal sealed partial class AllocCallbackLeaf : Component
{
    public Action? OnPick { get; set; }
    public Action<string>? OnName { get; set; }

    protected override Component? Render() => Div;
}

internal sealed partial class AllocCallbackEntryProbe : Component
{
    internal int Picks;

    protected override Component? Render() => Div[AllocCallbackLeaf.OnPick(Pick).OnName(Name)];

    private void Pick() => Picks++;

    private void Name(string value) => Picks++;
}

internal sealed partial class AllocCallbackFactoryProbe : Component
{
    internal int Picks;

    protected override Component? Render() =>
        Div()[Generated.AllocCallbackLeaf(OnPick: Pick, OnName: Name)];

    private void Pick() => Picks++;

    private void Name(string value) => Picks++;
}

// The delegate props the old `On`-dropping rule never reached — a render fragment (`Func<T, Component>`), not an
// event callback. Their setter shares their name like any other, and a fragment is NOT auto-wrapped —
// it does not return void or Task. A wrapper closure applied where the raw prop had none would show up
// here as a per-render delta.
internal sealed partial class AllocFragmentLeaf : Component
{
    public Func<int, Component>? Renderer { get; set; }

    protected override Component? Render() => Renderer is { } render ? render(1) : null;
}

internal sealed partial class AllocFragmentEntryProbe : Component
{
    protected override Component? Render() => Div[AllocFragmentLeaf.Renderer(Row)];

    private Component Row(int i) => Span[i.ToString(System.Globalization.CultureInfo.InvariantCulture)];
}

internal sealed partial class AllocFragmentFactoryProbe : Component
{
    protected override Component? Render() => Div()[Generated.AllocFragmentLeaf(Renderer: Row)];

    private Component Row(int i) => Span()[i.ToString(System.Globalization.CultureInfo.InvariantCulture)];
}

// The public READ surface. `OnPing?.Invoke()` is what a component calls its own callback back
// with, and it sits wherever the component chose to put it — including inside Render(), on the hot
// path. A null-conditional call on a delegate field is a branch and a call, and must stay that.
// The handlers are STATIC method groups so AutoCallback leaves them alone — the wrapped case has its own
// probe above, and this one is measuring the call, not the wrap.
internal sealed partial class AllocInvokeLeaf : Component
{
    public Action? OnPing { get; set; }
    public Action<string>? OnNamed { get; set; }

    protected override Component? Render()
    {
        OnPing?.Invoke();
        OnNamed?.Invoke("x");
        return Div;
    }
}

internal sealed partial class AllocInvokeEntryProbe : Component
{
    protected override Component? Render() => Div[AllocInvokeLeaf.OnPing(Ping).OnNamed(Named)];

    private static void Ping() { }

    private static void Named(string value) { }
}

internal sealed partial class AllocInvokeFactoryProbe : Component
{
    protected override Component? Render() => Div()[Generated.AllocInvokeLeaf(OnPing: Ping, OnNamed: Named)];

    private static void Ping() { }

    private static void Named(string value) { }
}

// A Head override, which the component's own render now produces (Component.RenderForLive) so that an
// entry built there is owned by the component whose Head it is. That puts a second chain — and a second
// set of pending resets — on the per-render path of every component that contributes to the head, so it
// gets the same parity pin as the body.
internal sealed partial class AllocHeadEntryProbe : Component
{
    protected override Component? HeadAssets => Meta.Name("probe").Content("keep");

    protected override Component? Render() => Div.Id("page")[Span["42"]];
}

internal sealed partial class AllocHeadFactoryProbe : Component
{
    protected override Component? HeadAssets => Rask.Core.Components.Generated.Meta(Name: "probe", Content: "keep");

    protected override Component? Render() => Div(Id: "page")[Span()["42"]];
}

// A component that overrides a lifecycle hook is the one shape the entry surface now claims a LiveState
// for at build time — it has to, because the deferred commit reads a missing LiveState as "not mine to
// notify" and a chain that names no folding prop would otherwise never mount. That claim is a real
// allocation, so it is pinned here rather than argued: the FACTORY already pays it (NotifyParameters is
// unconditional), and this asserts the entry pays no more. Ten of them, so a per-child difference cannot
// hide inside the harness noise.
internal sealed partial class AllocLifecycleLeaf : Component
{
    protected override void OnMount()
    {
    }

    protected override Component? Render() => Span["leaf"];
}

internal sealed partial class AllocLifecycleEntryProbe : Component
{
    protected override Component? Render() => Div[
        AllocLifecycleLeaf, AllocLifecycleLeaf, AllocLifecycleLeaf, AllocLifecycleLeaf, AllocLifecycleLeaf,
        AllocLifecycleLeaf, AllocLifecycleLeaf, AllocLifecycleLeaf, AllocLifecycleLeaf, AllocLifecycleLeaf
    ];
}

internal sealed partial class AllocLifecycleFactoryProbe : Component
{
    protected override Component? Render() => Div()[
        Generated.AllocLifecycleLeaf(), Generated.AllocLifecycleLeaf(), Generated.AllocLifecycleLeaf(),
        Generated.AllocLifecycleLeaf(), Generated.AllocLifecycleLeaf(), Generated.AllocLifecycleLeaf(),
        Generated.AllocLifecycleLeaf(), Generated.AllocLifecycleLeaf(), Generated.AllocLifecycleLeaf(),
        Generated.AllocLifecycleLeaf()
    ];
}

public class BuilderEntryAllocationPinTests
{
    [Fact]
    public void An_entry_built_lifecycle_component_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocLifecycleEntryProbe());
        var factory = Measure(static () => new AllocLifecycleFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    [Fact]
    public void An_entry_built_tree_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocEntryProbe());
        var factory = Measure(static () => new AllocFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    [Fact]
    public void An_entry_built_event_handler_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocEventEntryProbe());
        var factory = Measure(static () => new AllocEventFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    // A wrapped component callback (as opposed to the raw DOM handler above): the wrapper closure is the
    // dominant cost and both surfaces pay it, so nothing may add a second allocation on top.
    [Fact]
    public void An_entry_built_component_callback_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocCallbackEntryProbe());
        var factory = Measure(static () => new AllocCallbackFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    // A non-event delegate prop — a render fragment, reachable through an ordinary setter and never
    // auto-wrapped, so it must cost the same per render as the factory's assignment.
    [Fact]
    public void An_entry_built_fragment_delegate_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocFragmentEntryProbe());
        var factory = Measure(static () => new AllocFragmentFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    // Calling a callback back, per render, on both surfaces.
    [Fact]
    public void An_invoked_callback_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocInvokeEntryProbe());
        var factory = Measure(static () => new AllocInvokeFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    [Fact]
    public void An_entry_built_Head_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocHeadEntryProbe());
        var factory = Measure(static () => new AllocHeadFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    [Fact]
    public void A_bound_generic_entry_does_not_allocate_more_per_render_than_the_bound_factory()
    {
        var entry = Measure(static () => new AllocBoundEntryProbe());
        var factory = Measure(static () => new AllocBoundFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    // The pins above are RELATIVE, so a regression that hits BOTH surfaces passes all of them in
    // silence. This one is absolute: it fixes what the shape actually costs, measured 2026-08-08 on
    // 3e143905 at 1528 B/render for both surfaces (element handlers 2072 B, a bound control 3555 B on
    // the entry against the factory's 4709 B). Pinned at 1800 B — enough slack for jitter and a small
    // feature, tight enough that a per-render allocation added to the shared render path shows up here
    // rather than in a benchmark nobody ran.
    [Fact]
    public void The_shape_both_surfaces_share_costs_what_it_costs()
    {
        var entry = Measure(static () => new AllocEntryProbe());
        var factory = Measure(static () => new AllocFactoryProbe());

        Assert.InRange(entry, 0, 1800);
        Assert.InRange(factory, 0, 1800);
    }

    // One-sided: the entry surface is allowed to be CHEAPER (it is, for a bound control — it has one
    // entry where the factory has a none/sync/async fan-out), never more expensive. The 256 B of slack
    // covers measurement jitter; a per-render delegate or buffer would be well clear of it.
    private static void AssertNoWorseThan(long entry, long factory) =>
        Assert.True(entry - factory <= 256, $"entry {entry} B/render vs factory {factory} B/render");

    // Steady-state cost of ONE more render of an already-mounted tree: the host is built once and
    // re-rendered, so mount-time allocation is warmed away and only the per-render work is measured.
    private static long Measure(Func<Component> build)
    {
        var sp = RenderHarness.EmptyServices();
        var host = build();
        for (var i = 0; i < 200; i++)
        {
            Render(host, sp);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int iterations = 2000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            Render(host, sp);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private static void Render(Component host, IServiceProvider sp)
    {
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        ctx.NotifyParameters(resolved, propsChanged: true);
        _ = resolved.ToHtml();
    }
}
