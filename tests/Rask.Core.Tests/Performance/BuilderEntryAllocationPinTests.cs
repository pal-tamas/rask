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

    protected override Component? Render() => Div[Input(() => Model.Name).Id("name")];
}

internal sealed partial class AllocBoundFactoryProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    protected override Component? Render() =>
        Div()[Rask.Core.Components.Generated.Input(() => Model.Name, Id: "name")];
}

// The event surface, which is where the carriers live: every one of Element's ~88 handler pairs is a
// `Handler?`/`Handler<TArgs>?` prop, so a wired handler now goes through a struct wrap on the way in and
// the reset writes a struct on the way out. Both are supposed to be free — the carrier is a view over
// the same dictionary slot, never storage — and, critically, an element handler must stay UNWRAPPED: an
// AutoCallback wrapper would be one closure per handler per render, which is exactly what this pins.
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

// The other side of the carrier rule. A NON-Element component's callback stays AutoCallback-wrapped, so
// it costs one closure per handler per render — on BOTH surfaces, which is the parity being pinned. The
// carrier itself must add nothing on top: it is a struct, its nullable is a struct, and the setter's
// From() maps null to unset without touching the heap.
internal sealed partial class AllocCallbackLeaf : Component
{
    public Handler? OnPick { get; set; }
    public Handler<string>? OnName { get; set; }

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

public class BuilderEntryAllocationPinTests
{
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
    // dominant cost and both surfaces pay it, so the carrier must not add a second allocation on top.
    [Fact]
    public void An_entry_built_component_callback_does_not_allocate_more_per_render_than_the_factory()
    {
        var entry = Measure(static () => new AllocCallbackEntryProbe());
        var factory = Measure(static () => new AllocCallbackFactoryProbe());

        AssertNoWorseThan(entry, factory);
    }

    [Fact]
    public void A_bound_generic_entry_does_not_allocate_more_per_render_than_the_bound_factory()
    {
        var entry = Measure(static () => new AllocBoundEntryProbe());
        var factory = Measure(static () => new AllocBoundFactoryProbe());

        AssertNoWorseThan(entry, factory);
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
