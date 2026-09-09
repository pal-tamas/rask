namespace Rask.Core.Tests;

// The catalogue of every shape a children list can take, and which indexer overload serves it.
//
// The first group is what already worked and must keep working: [OverloadResolutionPriority] on the
// two typed indexers is the only thing holding these on the typed path now that a `params object?[]`
// overload exists, and `string` implements IEnumerable — so without the priority, "hi" would bind to
// the loose overload in NORMAL form (which beats the params EXPANDED form) and render one child per
// character. Every case in the first group is therefore a live regression guard, not documentation.
//
// The second group is what the loose overload adds. All of it comes from ONE cause: a chain ending at
// a STEP has the type Build<T>, and the implicit conversion that makes it a component never lifts
// through IEnumerable<>. A projection of chains could not be children.
internal sealed partial class CatalogBadge : Component
{
    public string? Label { get; set; }

    protected override Component? Render() => Em[Label ?? ""];
}

public partial class BuilderChildrenShapesTests : RaskMarkup
{
    // ---- Group 1: shapes that already bound, pinned against overload drift -------------------

    [Fact]
    public void A_string_child_is_ONE_text_node_not_one_per_character() =>
        Assert.Equal("<div>hi</div>", Div["hi"].ToHtml());

    [Fact]
    public void Components_listed_literally() =>
        Assert.Equal("<div><span>a</span><span>b</span></div>", Div[Span["a"], Span["b"]].ToHtml());

    [Fact]
    public void Nesting_is_not_collapsed_into_the_child_s_own_children() =>
        Assert.Equal("<div><span><em>x</em></span></div>", Div[Span[Em["x"]]].ToHtml());

    // A concatenation and an interpolation are both just `string`, so they ride the same implicit
    // operator as a literal — but only while the priority keeps them off the loose overload, which
    // would see the sequence-of-chars instead.
    [Fact]
    public void A_concatenated_string_stays_one_text_node()
    {
        var name = "world";
        Assert.Equal("<div>hello world</div>", Div["hello " + name].ToHtml());
    }

    [Fact]
    public void An_interpolated_string_stays_one_text_node()
    {
        var (key, value) = ("k", 1);
        Assert.Equal("<div>k=1</div>", Div[$"{key}={value}"].ToHtml());
    }

    [Fact]
    public void A_joined_string_stays_one_text_node() =>
        Assert.Equal("<div>a, b</div>", Div[string.Join(", ", ["a", "b"])].ToHtml());

    [Fact]
    public void Heterogeneous_literals() =>
        Assert.Equal("<div>Score: 42</div>", Div["Score: ", 42].ToHtml());

    [Fact]
    public void A_prebuilt_sequence_of_components()
    {
        IEnumerable<Component?> kids = [Span["a"], Span["b"]];
        Assert.Equal("<div><span>a</span><span>b</span></div>", Div[kids].ToHtml());
    }

    [Fact]
    public void A_projection_that_ENDS_AT_AN_INDEXER_was_always_fine() =>
        Assert.Equal(
            "<div><span data-rask-key=\"a\">a</span><span data-rask-key=\"b\">b</span></div>",
            Div[new[] { "a", "b" }.Select(s => Span.Key(s)[s])].ToHtml());

    [Fact]
    public void A_null_child_renders_nothing_so_a_conditional_needs_no_placeholder()
    {
        Component? absent = null;
        Assert.Equal("<div><span>a</span></div>", Div[Span["a"], absent].ToHtml());
    }

    // ---- Group 2: what the loose overload adds ----------------------------------------------

    [Fact]
    public void A_projection_of_chains_ENDING_AT_A_STEP() =>
        Assert.Equal(
            "<div><em data-rask-key=\"a\">a</em><em data-rask-key=\"b\">b</em></div>",
            Div[new[] { "a", "b" }.Select(s => CatalogBadge.Key(s).Label(s))].ToHtml());

    [Fact]
    public void Literals_and_a_projection_MIXED_in_one_list() =>
        Assert.Equal(
            "<div>Showing <em data-rask-key=\"a\">a</em><em data-rask-key=\"b\">b</em> of 2</div>",
            Div["Showing ", new[] { "a", "b" }.Select(s => CatalogBadge.Key(s).Label(s)), " of ", 2].ToHtml());

    [Fact]
    public void Several_sequences_side_by_side_without_Concat()
    {
        IEnumerable<Component?> head = [Span["h"]];
        IEnumerable<Component?> tail = [Span["t"]];
        Assert.Equal("<div><span>h</span><em data-rask-key=\"x\">x</em><span>t</span></div>",
            Div[head, new[] { "x" }.Select(s => CatalogBadge.Key(s).Label(s)), tail].ToHtml());
    }

    [Fact]
    public void A_nested_projection_flattens_so_SelectMany_is_optional()
    {
        string[][] groups = [["a", "b"], ["c"]];
        Assert.Equal(
            "<div><em data-rask-key=\"a\">a</em><em data-rask-key=\"b\">b</em><em data-rask-key=\"c\">c</em></div>",
            Div[groups.Select(g => g.Select(s => CatalogBadge.Key(s).Label(s)))].ToHtml());
    }

    [Fact]
    public void A_projection_of_interpolated_strings_renders_as_text() =>
        Assert.Equal(
            "<div>a!b!</div>",
            Div[new[] { "a", "b" }.Select(s => $"{s}!")].ToHtml());

    [Fact]
    public void A_sequence_of_plain_values_renders_as_text() =>
        Assert.Equal("<div>abc</div>", Div[new[] { "a", "b", "c" }.AsEnumerable()].ToHtml());

    [Fact]
    public void A_null_alongside_a_projection_still_renders_nothing()
    {
        Component? absent = null;
        Assert.Equal(
            "<div><em data-rask-key=\"a\">a</em></div>",
            Div[absent, new[] { "a" }.Select(s => CatalogBadge.Key(s).Label(s))].ToHtml());
    }

    // The compile error the loose overload gives away: an element that cannot be a child is no longer
    // rejected by the compiler, so it has to be rejected here, naming the type.
    [Fact]
    public void An_element_that_cannot_be_a_child_throws_and_names_its_type()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Div[new[] { 1 }.Select(_ => new MemoryStream())].ToHtml());

        Assert.Contains("System.IO.MemoryStream", error.Message, StringComparison.Ordinal);
    }
}
