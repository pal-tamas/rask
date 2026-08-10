namespace Rask.Bootstrap.Tests;

// PROTOTYPE — the builder setters on the Bootstrap layer, which is the library that exercises the
// INTERMEDIATE-base case: a Bs component inherits Id/Class from BsBlock and the shared field props
// (Label/Disabled/Required/Size/HelpText/Name/Floating) from BsFormControl<T>, neither of which is part
// of the Element/Component chain the generator writes once as constrained generic extensions. Those
// props used to get no setter at all — the chain simply would not compile — so these pin that a Bs
// control can be built end to end without falling back to the factory.
public partial class BsBuilderSetterTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_prop_inherited_from_BsBlock_gets_a_setter() =>
        Assert.Equal(
            "<span id=\"tag\" class=\"badge text-bg-success\">New</span>",
            BsBadge.Id("tag").Color(BsColor.Success)["New"].ToHtml());

    // The receiver stays the concrete component, so an inherited setter can be chained into an own
    // one and back — a BsBlock-typed extension would return the base and end the chain.
    [Fact]
    public void An_inherited_setter_chains_with_the_components_own() =>
        Assert.Equal(
            "<button class=\"btn btn-primary btn-lg w-100\" type=\"button\">Save</button>",
            BsButton.Color(BsColor.Primary).Class("w-100").Size(BsSize.Lg)["Save"].ToHtml());

    // BsFormControl<T> sits between the control and BsBlock, so its props are two levels up — and the
    // control is generic, so the setter carries the type parameter through.
    [Fact]
    public void A_prop_inherited_from_BsFormControl_gets_a_setter()
    {
        var model = new CheckModel();

        var html = BsCheck.Bind(() => model.Done).Label("Agree").Disabled(true).ToHtml();

        Assert.Contains("form-check-input", html, StringComparison.Ordinal);
        Assert.Contains("Agree", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
    }

    // A Bs callback prop rides a carrier now (so its setter is `.OnClick(…)`, not `.Click(…)`), and the
    // one thing that must NOT change with it: a non-Element component's callback stays AutoCallback-
    // wrapped. A Bs control is not an Element, so there is no DOM handler-owner resolution to re-render
    // the consumer — drop the wrapper and the handler runs, the state changes, and nothing repaints,
    // with byte-identical markup. Both surfaces, because they have to agree.
    [Fact]
    public void A_bs_callback_stays_auto_wrapped_on_both_surfaces()
    {
        var host = ClickHost;
        var raw = (Rask.Core.Callback)host.Bump;

        Assert.NotSame(raw, BsButton.OnClick(raw).OnClick?.Fn);
        Assert.NotSame(raw, BsButton.OnClick(raw).OnClick?.Fn);

        // …and an unowned handler is still handed through untouched, so the wrap is genuinely
        // AutoCallback's decision rather than an unconditional closure per render.
        Rask.Core.Callback orphan = Noop;
        Assert.Same(orphan, BsButton.OnClick(orphan).OnClick?.Fn);
    }

    // An omitted callback must read back as unset: BsToast starts its auto-hide timer only when OnClose
    // is wired, and BsDataGrid's controlled-mode gates are all `is not null` tests on their callbacks.
    [Fact]
    public void An_omitted_bs_callback_reads_back_as_unset()
    {
        Rask.Core.Callback? maybe = null;

        Assert.Null(BsButton.OnClick);
        Assert.Null(BsButton.OnClick(maybe).OnClick);
        Assert.Null(BsButton.OnClick(maybe).OnClick);
    }

    // The carrier rule the `On` prefix never reached. A raw delegate prop is INVOCABLE, so
    // `grid.RowClass(fn)` bound to the property — the same-named setter could not be reached at all,
    // and the prop had no way to be set from a chain. These props ride a carrier now, which is exactly
    // what makes this call compile: it is the setter, not an attempt to invoke `Func<Supplier, string?>`
    // with a `Func<Supplier, string?>`.
    [Fact]
    public void A_non_On_delegate_prop_can_be_set_from_the_chain()
    {
        List<BsColumn<Supplier>> columns =
        [
            new BsColumn<Supplier> { Title = "Name", Value = s => s.Name },
        ];

        var html = BsDataGrid(Data: Suppliers, Columns: columns)
            .RowKey(s => s.Id)
            .RowClass(s => s.Name == "Acme" ? "table-warning" : null)
            .ExpandedContent(s => Div[s.Name])
            .ToHtml();

        Assert.Contains("table-warning", html, StringComparison.Ordinal);
    }

    // …and the half of the carrier that fails silently: an OMITTED delegate must read back as unset.
    // The carrier's implicit conversion accepts a null delegate and hands back a NON-null carrier
    // wrapping null, so a factory call with no ExpandedContent would answer `Expandable` true and grow
    // an expander column on every row. `From` is what keeps null null; every generated assignment goes
    // through it.
    [Fact]
    public void An_omitted_non_On_delegate_prop_reads_back_as_unset()
    {
        List<BsColumn<Supplier>> columns =
        [
            new BsColumn<Supplier> { Title = "Name", Value = s => s.Name },
        ];
        Func<Supplier, Rask.Core.Component?>? none = null;

        Assert.Null(BsDataGrid(Data: Suppliers, Columns: columns).ExpandedContent);
        Assert.Null(BsDataGrid(Data: Suppliers, Columns: columns, ExpandedContent: none).ExpandedContent);
        Assert.Null(BsDataGrid(Data: Suppliers, Columns: columns).ExpandedContent(none).ExpandedContent);

        // The expander column is gated on that answer, so a non-null carrier wrapping null would grow
        // a leading header cell nobody asked for — visible in the markup, invisible in the type.
        Assert.Equal(
            BsDataGrid(Data: Suppliers, Columns: columns).ToHtml(),
            BsDataGrid(Data: Suppliers, Columns: columns, ExpandedContent: none).ToHtml());
        Assert.Contains("<th scope=\"col\"></th>",
            BsDataGrid(Data: Suppliers, Columns: columns, ExpandedContent: s => Div[s.Name]).ToHtml(),
            StringComparison.Ordinal);
    }

    // The three Bootstrap components a RASK001-required prop used to lock out of the builder surface
    // entirely: BsIcon.Name (an enum), BsProgress.Value and BsCheck.Value. "No entry" meant "no way to
    // build it at all" once the factory goes, so this is the shape that had to compile before stage E
    // could proceed — and it is the only place the entries themselves are exercised, since an entry is
    // a member of Component and only in scope inside a component body.
    [Fact]
    public void The_three_controls_with_a_required_prop_build_through_a_chain()
    {
        var html = BsRequiredPropProbe.ToHtml();

        Assert.Contains("bi bi-star", html, StringComparison.Ordinal);
        Assert.Contains("aria-valuenow=\"42\"", html, StringComparison.Ordinal);
        Assert.Contains("form-check-input", html, StringComparison.Ordinal);
        Assert.Contains("Agree", html, StringComparison.Ordinal);

        // The bound half of the same probe. Its only real assertion is that the file compiles: RASK038
        // is an Error, so a chain it wrongly flags cannot reach a test body at all.
        Assert.Contains("Bound", html, StringComparison.Ordinal);

        // …and the two whose `required` modifier used to withhold an entry entirely.
        Assert.Contains("Saved", html, StringComparison.Ordinal);
        Assert.Contains("Orders", html, StringComparison.Ordinal);
    }

    // A bound chain must not be asked for `Value`, and giving BsCheck.Value the `= false` initializer
    // its comment always claimed is what makes that true — the same default the control renders anyway.
    [Fact]
    public void A_bound_check_defaults_Value_rather_than_requiring_it() =>
        Assert.Equal(BsCheck.Value(false).ToHtml(), BsCheck.ToHtml());

    private static readonly Supplier[] Suppliers = [new(1, "Acme"), new(2, "Globex")];

    private sealed record Supplier(int Id, string Name);

    private static void Noop() { }

    private sealed class CheckModel
    {
        public bool Done { get; set; }
    }

}

// Builds the three required-prop controls through their entries. Every setter here names a prop the
// chain must set, so RASK038 stays quiet — which is the other half of the same change, and the half
// that only works because Rask.Bootstrap PUBLISHES its requiredness: from here BsIcon.Name is a
// metadata symbol whose member initializer, if it had one, would be invisible.
//
// BsCheck appears twice, controlled and BOUND, because those two chains name different props. `Value`
// is required on the controlled factory and excluded from the bound one, and RASK038 reads a single
// entry, so it cannot model that split: it used to report `BsCheck.Bind(…)` as never setting `Value`
// even though Render only reads Value when Bind is null. The fix is on this side rather than in the
// analyzer — Value carries the `= false` initializer its own comment already described, which makes it
// optional on both chains. Delete that initializer and this file stops compiling.
internal sealed partial class BsRequiredPropProbe : Rask.Core.Component
{
    private readonly BoundModel _model = new();

    protected override Rask.Core.Component? Render() =>
        Div[
            BsIcon.Name(BsIconName.Star),
            BsProgress.Value(42),
            BsCheck.Value(true).Label("Agree"),
            BsCheck.Bind(() => _model.Done).Label("Bound"),

            // The `required` MODIFIER, which used to withhold an entry outright: a type with a required
            // member does not satisfy `new()` (CS9040), so these two had no builder surface at all and
            // would have ceased to exist the day the factory is deleted. Construction goes through
            // ActivatorUtilities now — requiredness is a compile-time check, so it may build what `new T()`
            // may not — and what enforces the value is RASK038 on this very chain. Drop either setter and
            // this file stops compiling.
            BsToast.Id(1).Message("Saved"),
            BsStat.Value("42").Label("Orders")
        ];

    internal sealed class BoundModel
    {
        public bool Done { get; set; }
    }
}

// A Component so DelegateOwner can resolve an owner for the method group — which is the precondition
// AutoCallback checks before it wraps anything.
internal sealed partial class ClickHost : Rask.Core.Component
{
    internal void Bump() { }

    protected override Rask.Core.Component? Render() => null;
}
