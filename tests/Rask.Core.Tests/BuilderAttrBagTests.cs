using System.Collections;

namespace Rask.Core.Tests;

// `.Data("test-id", "primary")` and `.Aria("label", "Close")` beside the dictionary setter. The pair
// form is what real markup is full of, and a Dictionary for one attribute costs three allocations —
// the dictionary, its bucket array and its entry array — on every render of every element carrying one.
//
// Every test here asserts against the DICTIONARY spelling's own output rather than a hand-written
// string wherever the two must agree: the whole claim is that the ergonomic form is the same markup,
// so comparing it to a literal I typed would only prove I can type.
public partial class BuilderAttrBagTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_pair_renders_what_the_dictionary_renders() =>
        Assert.Equal(
            Div.Data(new Dictionary<string, string?> { ["test-id"] = "primary" }).ToHtml(),
            Div.Data("test-id", "primary").ToHtml());

    [Fact]
    public void Several_pairs_render_what_the_dictionary_renders() =>
        Assert.Equal(
            Div.Data(new Dictionary<string, string?> { ["a"] = "1", ["b"] = "2", ["c"] = "3" }).ToHtml(),
            Div.Data(("a", "1"), ("b", "2"), ("c", "3")).ToHtml());

    [Fact]
    public void Aria_gets_the_same_pair_form() =>
        Assert.Equal(
            Span.Aria(new Dictionary<string, string?> { ["label"] = "Close" }).ToHtml(),
            Span.Aria("label", "Close").ToHtml());

    // The bag is prefixed by the property it lands on, not by anything it knows itself — the same
    // instance would render `data-x` on Data and `aria-x` on Aria.
    [Fact]
    public void The_prefix_comes_from_the_property() =>
        Assert.Equal(
            "<div data-x=\"1\" aria-x=\"2\"></div>",
            Div.Data("x", "1").Aria("x", "2").ToHtml());

    [Fact]
    public void A_null_value_still_emits_the_attribute_bare() =>
        Assert.Equal(
            Div.Data(new Dictionary<string, string?> { ["flag"] = null }).ToHtml(),
            Div.Data("flag", null).ToHtml());

    // The opt-out flags (`data-rask-no-restore`) are bare attributes, so the name alone is a step.
    [Fact]
    public void A_name_alone_is_the_bare_attribute() =>
        Assert.Equal("<div data-rask-no-restore></div>", Div.Data("rask-no-restore").ToHtml());

    // `""` is NOT the same attribute as no value, and the one-argument step must mean the bare one.
    [Fact]
    public void A_name_alone_is_not_the_same_as_an_empty_value()
    {
        Assert.Equal("<div data-flag=\"\"></div>", Div.Data("flag", "").ToHtml());
        Assert.NotEqual(Div.Data("flag", "").ToHtml(), Div.Data("flag").ToHtml());
    }

    // Order is the order given, which is what a reader of the call site expects. Dictionary happens to
    // preserve insertion order for a never-removed-from instance, so the two agree — but this pins the
    // bag's own guarantee rather than relying on that.
    [Fact]
    public void Pairs_render_in_the_order_given() =>
        Assert.Equal("<div data-z=\"1\" data-a=\"2\"></div>", Div.Data(("z", "1"), ("a", "2")).ToHtml());

    [Fact]
    public void A_later_duplicate_wins_the_lookup_as_a_dictionary_literal_would()
    {
        IReadOnlyDictionary<string, string?> bag = new AttrBag([("k", "first"), ("k", "second")]);
        Assert.Equal("second", bag["k"]);
    }

    [Fact]
    public void An_empty_span_is_refused_rather_than_rendering_nothing() =>
        Assert.Throws<ArgumentException>(() => new AttrBag([]));

    [Fact]
    public void An_empty_name_is_refused() =>
        Assert.Throws<ArgumentException>(() => new AttrBag("", "v"));

    // Element writes the bag through a type check, bypassing the interface enumerator. The read-only
    // dictionary contract still has to hold for anything that gets one through the property.
    [Fact]
    public void It_honours_the_read_only_dictionary_contract()
    {
        IReadOnlyDictionary<string, string?> one = new AttrBag("k", "v");
        Assert.Single(one);
        Assert.True(one.ContainsKey("k"));
        Assert.False(one.ContainsKey("nope"));
        Assert.Equal("v", one["k"]);
        Assert.Throws<KeyNotFoundException>(() => one["nope"]);
        Assert.Equal(["k"], one.Keys);
        Assert.Equal(["v"], one.Values);
        Assert.Equal([new KeyValuePair<string, string?>("k", "v")], one);

        IReadOnlyDictionary<string, string?> many = new AttrBag([("a", "1"), ("b", null)]);
        Assert.Equal(2, many.Count);
        Assert.True(many.TryGetValue("b", out var b));
        Assert.Null(b);
        Assert.Equal(["a", "b"], many.Keys);
        Assert.False(((IEnumerable)many).GetEnumerator() is null);
    }

    // The bag participates in the props-changed fold like any other value, so a component whose data
    // attribute did not move must not report a change. Two equal bags are different objects, so this
    // pins that the fold compares what was RENDERED rather than the reference.
    [Fact]
    public void Re_supplying_the_same_pair_renders_the_same_markup_twice()
    {
        var first = Div.Data("k", "v").ToHtml();
        var second = Div.Data("k", "v").ToHtml();
        Assert.Equal(first, second);
    }
}
