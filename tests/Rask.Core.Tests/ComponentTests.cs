#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests;

public partial class ComponentTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rendering_with_no_props_and_no_children_gives_open_and_close_tags() =>
        Assert.Equal("<block></block>", new Block().ToHtml());

    [Fact]
    public void Rendering_with_no_props_and_a_string_child_encodes_the_child_text() =>
        Assert.Equal("<block>&lt;x&gt;</block>", new Block()["<x>"].ToHtml());

    [Fact]
    public void Rendering_a_prop_with_a_string_value_emits_a_quoted_attribute() =>
        Assert.Equal(
            "<block class=\"x\"></block>",
            new Block { Class = "x" }.ToHtml());

    [Fact]
    public void Rendering_an_attribute_value_with_specials_encodes_it() =>
        Assert.Equal(
            "<block class=\"a&quot;b&lt;c\"></block>",
            new Block { Class = "a\"b<c" }.ToHtml());

    [Fact]
    public void Multiple_children_render_in_declaration_order()
    {
        var html = new Block()[Text.Value("a"), Raw.Value("<i>"), Text.Value("b")].ToHtml();

        Assert.Equal("<block>a<i>b</block>", html);
    }

    [Fact]
    public void A_null_children_argument_is_treated_as_empty()
    {
        var html = new Block().ToHtml();

        Assert.Equal("<block></block>", html);
    }

    [Fact]
    public void A_self_closing_element_with_no_children_renders_a_self_closing_tag() =>
        Assert.Equal("<void />", new VoidEl().ToHtml());

    [Fact]
    public void A_self_closing_element_with_children_supplied_still_self_closes_and_ignores_them()
    {
        var html = new VoidEl()[Text.Value("ignored")].ToHtml();

        Assert.Equal("<void />", html);
    }

    [Fact]
    public void A_self_closing_element_places_its_attributes_before_the_self_closer() =>
        Assert.Equal(
            "<void class=\"a\" />",
            new VoidEl { Class = "a" }.ToHtml());

    [Fact]
    public void The_indexer_assigns_the_children_and_hands_back_the_same_component()
    {
        var div = Div;
        var returned = div[Text.Value("a")];

        Assert.Same(div, returned);
        Assert.NotNull(div.Children);
        Assert.Single(div.Children!);
    }

    [Fact]
    public void The_html_rendered_after_the_indexer_contains_the_children()
    {
        var html = Div[Span, "hi"].ToHtml();

        Assert.Equal("<div><span></span>hi</div>", html);
    }

    [Fact]
    public void The_indexer_accepts_a_string_and_a_component_via_the_implicit_child()
    {
        // Both `"text"` and `Component` instances flow through the indexer thanks to the
        // implicit Component conversions; the indexer signature is `params IEnumerable<Component>`.
        var html = Div["before", Strong["bold"], "after"].ToHtml();

        Assert.Equal("<div>before<strong>bold</strong>after</div>", html);
    }

    private sealed class Block : Element
    {
        protected override string TagName => "block";
    }

    private sealed class VoidEl : Element
    {
        protected override string TagName => "void";
        protected override bool SelfClosing => true;
    }
}
