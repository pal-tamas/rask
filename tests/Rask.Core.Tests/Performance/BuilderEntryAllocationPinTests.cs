using Rask.Core.Live;

#pragma warning disable RASK014 // the pin needs the very instance it hands to the render context

namespace Rask.Core.Tests.Performance;

// What a render of each markup shape costs, per render, in bytes.
//
// The entry's omitted-prop reset (BuilderRuntime) is the hot-path addition: every entry pushes a slot
// carrying its pending mask and the per-type reset to run at the end of the parent's Render(). None of
// that may allocate in the steady state — the slot stack is a per-thread buffer reused across renders,
// and the reset routines are handed over as method groups, which the compiler caches in a static
// field. A per-render delegate or a fresh list would show up here immediately.
//
// These were relative pins while a second surface existed to compare against. They are ABSOLUTE now:
// each ceiling was measured and then given headroom for jitter and a small feature, tight enough that a
// per-render allocation added to the shared render path shows up here rather than in a benchmark nobody
// ran. Re-measure and move a number deliberately; do not raise one to make a red test green.
internal sealed partial class AllocEntryProbe : Component
{
    protected override Component? Render() =>
        Div.Id("counter").Class("counter")[
            Span.Class("value")["42"],
            Button.Class("inc")["+"]
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

// The same control in CONTROLLED mode. Pinned so the bound number above decomposes: whatever the bound
// probe costs over this one is the binding, and a regression in the shared element path moves both.
internal sealed partial class AllocControlledInputProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    protected override Component? Render() => Div[Input.Value(Model.Name).Id("name")];
}

// The bound control with the bind expression HOISTED into a field, so it is built once instead of on
// every render. This is the pin that actually guards Rask's own bind path: the difference between this
// and AllocBoundEntryProbe is the C# compiler constructing an Expression<Func<T>> at the call site,
// which is not code this repo can make cheaper.
internal sealed partial class AllocHoistedBoundEntryProbe : Component
{
    internal readonly BoundForm Model = new() { Name = "Ada", Age = 36 };

    private readonly System.Linq.Expressions.Expression<Func<string>> _bind;

    public AllocHoistedBoundEntryProbe() => _bind = () => Model.Name;

    protected override Component? Render() => Div[Input.Bind(_bind).Id("name")];
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

// A Head override, which the component's own render now produces (Component.RenderForLive) so that an
// entry built there is owned by the component whose Head it is. That puts a second chain — and a second
// set of pending resets — on the per-render path of every component that contributes to the head, so it
// gets the same parity pin as the body.
internal sealed partial class AllocHeadEntryProbe : Component
{
    protected override Component? HeadAssets => Meta.Name("probe").Content("keep");

    protected override Component? Render() => Div.Id("page")[Span["42"]];
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

public class BuilderEntryAllocationPinTests
{
    [Fact]
    public void An_entry_built_lifecycle_component_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocLifecycleEntryProbe());

        // 1528 B/render measured 2026-08-24; pinned at 1800 B.
        AssertCosts(cost, 1800);
    }

    [Fact]
    public void An_entry_built_tree_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocEntryProbe());

        // 1528 B/render measured 2026-08-24; pinned at 1800 B.
        AssertCosts(cost, 1800);
    }

    [Fact]
    public void An_entry_built_event_handler_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocEventEntryProbe());

        // 1728 B/render measured 2026-08-24; pinned at 2000 B.
        AssertCosts(cost, 2000);
    }

    // A wrapped component callback (as opposed to the raw DOM handler above): the wrapper closure is the
    // dominant cost and both surfaces pay it, so nothing may add a second allocation on top.
    [Fact]
    public void An_entry_built_component_callback_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocCallbackEntryProbe());

        // 1464 B/render measured 2026-08-24; pinned at 1700 B.
        AssertCosts(cost, 1700);
    }

    // A non-event delegate prop — a render fragment, reachable through an ordinary setter and never
    // auto-wrapped, so it must cost the same per render as the factory's assignment.
    [Fact]
    public void An_entry_built_fragment_delegate_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocFragmentEntryProbe());

        // 1208 B/render measured 2026-08-24; pinned at 1400 B.
        AssertCosts(cost, 1400);
    }

    // Calling a callback back, per render, on both surfaces.
    [Fact]
    public void An_invoked_callback_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocInvokeEntryProbe());

        // 1208 B/render measured 2026-08-24; pinned at 1400 B.
        AssertCosts(cost, 1400);
    }

    [Fact]
    public void An_entry_built_Head_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocHeadEntryProbe());

        // 1576 B/render measured 2026-08-24; pinned at 1800 B.
        AssertCosts(cost, 1800);
    }

    // The bound control, decomposed. One aggregate ceiling could say a number had moved but never which
    // layer moved it, and #793 was opened on exactly that: 3555 B/render recorded 2026-08-08, 5163 B/render
    // when it was next looked at, with nothing in between able to say where it went.
    //
    // Measured 2026-08-24, after removing the Accessor's redundant getter/setter delegates:
    //
    //     bare Div                                   1088
    //     Div[Input.Of<string>().Id]                 1192
    //     Div[Input.Value(...).Id]      controlled   1216
    //     Div[Input.Bind(hoisted).Id]   bound        2723
    //     Div[Input.Bind(() => ...).Id] bound        5034
    //
    // Two things fall straight out of that, and both are why 3555 is not a baseline to chase:
    //
    //   * 2311 B — 46% of the total — is the C# compiler building an Expression<Func<T>> at the CALL SITE
    //     on every render. It is not Rask code and no change here can make it cheaper; the only lever is
    //     the shape of the public Bind API. So this probe cannot go below ~3500 B however good the bind
    //     path gets, which is already above the 3555 the comment recorded — the old number cannot have
    //     been measuring this same shape.
    //   * 1507 B is Rask's own bind path (bound-hoisted minus controlled): parsing the expression, the
    //     auto-created EditContext, two registered handlers and the validator registration.
    //
    // All three ceilings are ABSOLUTE. A relative pin is what let the regression hide in the first place
    // (the factory arm grew alongside the entry arm and the difference stayed inside its slack), so these
    // decompose the cost without reintroducing that: a regression in the shared element path trips the
    // controlled pin, one in the bind path trips the hoisted pin, and one in either trips the aggregate.
    [Fact]
    public void A_controlled_input_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocControlledInputProbe());

        // 1216 B/render measured 2026-08-24; pinned at 1400 B.
        AssertCosts(cost, 1400);
    }

    [Fact]
    public void A_bound_input_costs_what_it_costs_apart_from_its_expression_tree()
    {
        var cost = Measure(static () => new AllocHoistedBoundEntryProbe());

        // 2723 B/render measured 2026-08-24; pinned at 3000 B. This is the one to watch — it is the only
        // one of the three that moves when Rask's bind path changes.
        AssertCosts(cost, 3000);
    }

    [Fact]
    public void A_bound_generic_entry_costs_what_it_costs()
    {
        var cost = Measure(static () => new AllocBoundEntryProbe());

        // 5034 B/render measured 2026-08-24; pinned at 5300 B, down from the 5600 #793 recorded.
        AssertCosts(cost, 5300);
    }

    private static void AssertCosts(long actual, long ceiling) =>
        Assert.True(actual <= ceiling, $"{actual} B/render, pinned at {ceiling} B");

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
