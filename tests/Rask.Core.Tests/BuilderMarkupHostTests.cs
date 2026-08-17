namespace Rask.Core.Tests;

// PROTOTYPE — how code that is NOT a component reaches the builder surface.
//
// Entries are members of the enclosing type (or inherited into it) rather than `using static` imports,
// because a static-imported property loses to a same-named type in scope (CS0119). The consequence is
// that the surface is only reachable from inside a type that HAS them — and a quarter of this repo's
// call sites are in types that are not components: test classes, fixtures, factories of demo
// components.
//
// `RaskMarkup` is that type: Component's own base, carrying the framework entries and nothing else. A
// test class derives from it and gets the same `Div` a component gets, by the same rule, with no
// Render(), no lifecycle, no positional identity and no render cache.
//
// A user's own components still cannot ride on it (a generator cannot add members to a referenced
// assembly's type), so those are injected into the markup host's own `partial`, exactly as they are
// into a component's — which is why the host is `partial`.
internal sealed partial class MarkupChip : Component
{
    public new string? Text { get; set; }

    protected override Component? Render() => Em[Text ?? ""];
}

public partial class BuilderMarkupHostTests : RaskMarkup
{
    // A markup host is a type, so its static state is ordinary static state. This is the DemoRegistry
    // shape — markup built inside a lambda, held in a lookup table — and the initializer runs in a
    // static context, which reaches an inherited `protected static` entry the same as any other.
    private static readonly Dictionary<string, Func<Component>> _demos = new(StringComparer.Ordinal)
    {
        ["badge"] = () => Div.Class("wrap")[MarkupChip.Text("new")],
    };

    // The render-fragment shape: a delegate, which a component cannot be.
    private static readonly Func<IReadOnlyList<string>, Component> _template =
        messages => Div[messages.Select(static m => Span.Key(m).Class("msg")[m])];

    [Fact]
    public void A_test_class_reaches_the_framework_entries() =>
        Assert.Equal("<div><strong>hi</strong></div>", Div[Strong["hi"]].ToHtml());

    [Fact]
    public void A_test_class_reaches_its_own_assemblys_components() =>
        Assert.Equal("<div class=\"wrap\"><em>new</em></div>", _demos["badge"]().ToHtml());

    [Fact]
    public void A_delegate_field_reaches_them_too() =>
        // The rows are keyed (RASK022), so each carries its data-rask-key — the attribute order is the
        // fixed one: class before data-*.
        Assert.Equal(
            "<div><span class=\"msg\" data-rask-key=\"a\">a</span>"
            + "<span class=\"msg\" data-rask-key=\"b\">b</span></div>",
            _template(["a", "b"]).ToHtml());

    // A `static class` cannot derive from anything, so it cannot be a markup host. It does not have to
    // be: simple-name lookup walks OUT through enclosing types, so a static class nested in a markup
    // host sees the host's entries — which is the whole of what a markup-building static class needs.
    internal static class Nested
    {
        internal static Component Build() => Div[Strong["nested"]];
    }

    [Fact]
    public void A_nested_static_class_reaches_the_enclosing_hosts_entries() =>
        Assert.Equal("<div><strong>nested</strong></div>", Nested.Build().ToHtml());

    // The entry must not shadow the type: `MarkupChip` still names a type here, next to the entry that
    // builds one. That is the whole reason entries are properties rather than the factory's methods.
    [Fact]
    public void The_type_stays_nameable_alongside_its_entry() => Assert.Equal(typeof(MarkupChip), Probe());

    private static Type Probe() => typeof(MarkupChip);
}
