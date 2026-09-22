using Rask.Core;

namespace Rask.Core.Tests.Components;

// MIGRATED to the builder surface. A test class is not a component, so the entries do not reach it by
// the rule that reaches a component — `: RaskMarkup` is what puts them in scope, and it is the whole
// of the change besides rewriting each call as a chain.
public partial class DivTests : RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() => Assert.Equal("<div></div>", Div.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></div>",
            Div.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() => Assert.Equal("<div>&lt;x&gt;</div>", Div["<x>"].ToHtml());

    [Fact]
    public void Without_accessibility_props_no_aria_role_or_tabindex_is_emitted() =>
        Assert.Equal("<div></div>", Div.ToHtml());

    [Fact]
    public void Aria_emits_aria_prefixed_attributes() =>
        Assert.Equal(
            "<div aria-label=\"Close\" aria-expanded=\"true\"></div>",
            Div.Aria(new Dictionary<string, string?> { ["label"] = "Close", ["expanded"] = "true" }).ToHtml());

    [Fact]
    public void A_null_aria_value_emits_a_bare_attribute() =>
        Assert.Equal(
            "<div aria-hidden></div>",
            Div.Aria(new Dictionary<string, string?> { ["hidden"] = null }).ToHtml());

    [Fact]
    public void An_aria_value_is_html_encoded() =>
        Assert.Equal(
            "<div aria-label=\"a &amp; b\"></div>",
            Div.Aria(new Dictionary<string, string?> { ["label"] = "a & b" }).ToHtml());

    [Fact]
    public void Role_and_tabindex_emit_native_attributes() =>
        Assert.Equal(
            "<div role=\"dialog\" tabindex=\"-1\"></div>",
            Div.Role("dialog").TabIndex(-1).ToHtml());

    [Fact]
    public void Accessibility_props_follow_data_in_the_documented_order() =>
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" role=\"dialog\" tabindex=\"0\" aria-label=\"L\"></div>",
            Div
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" })
                .Role("dialog")
                .TabIndex(0)
                .Aria(new Dictionary<string, string?> { ["label"] = "L" })
                .ToHtml());

    [Fact]
    public void Title_emits_the_global_tooltip_attribute() =>
        Assert.Equal("<div title=\"2026-01-01 12:00:00Z\"></div>", Div.Title("2026-01-01 12:00:00Z").ToHtml());

    [Fact]
    public void The_title_is_encoded() =>
        Assert.Equal("<div title=\"a &amp; &lt;b&gt;\"></div>", Div.Title("a & <b>").ToHtml());

    [Fact]
    // Title joins the plain global attributes after style, ahead of the prefixed data-*/aria-* groups.
    public void The_title_sits_after_style_and_before_data() =>
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" title=\"t\" data-k=\"v\" role=\"dialog\" tabindex=\"0\" aria-label=\"L\"></div>",
            Div
                .Id("i")
                .Class("c")
                .Style("s")
                .Title("t")
                .Data(new Dictionary<string, string?> { ["k"] = "v" })
                .Role("dialog")
                .TabIndex(0)
                .Aria(new Dictionary<string, string?> { ["label"] = "L" })
                .ToHtml());

    [Fact]
    // An unset Title must emit nothing — every element in the framework gained this property, and any
    // stray attribute would change the rendered output (and the diff) of every existing page.
    public void An_unset_title_emits_nothing() => Assert.Equal("<div></div>", Div.ToHtml());

    // The escape hatch (#693). Emits last in the universal block, after aria-*, before tag-specific.
    [Fact]
    public void Verbatim_attributes_emit_after_the_aria_group() =>
        Assert.Equal(
            "<div id=\"i\" role=\"note\" aria-label=\"a\" lang=\"fr\" dir=\"rtl\"></div>",
            Div
                .Id("i")
                .Role("note")
                .Aria(new Dictionary<string, string?> { ["label"] = "a" })
                .Attributes(new Dictionary<string, string?> { ["lang"] = "fr", ["dir"] = "rtl" })
                .ToHtml());

    // WCAG 3.1.2 Language of Parts: a phrase in another language marked on the element that changes
    // language. Before the escape hatch this was unwritable anywhere but <html>.
    [Fact]
    public void A_lang_on_a_phrase_is_expressible() =>
        Assert.Equal(
            // non-ASCII text is encoded by HtmlSerializer's safe-ASCII rule, hence the entities
            "<div lang=\"fr\">d&#xE9;j&#xE0; vu</div>",
            Div.Attributes(new Dictionary<string, string?> { ["lang"] = "fr" })["déjà vu"].ToHtml());

    [Fact]
    public void A_null_valued_attribute_is_emitted_bare() =>
        Assert.Equal(
            "<div hidden></div>",
            Div.Attributes(new Dictionary<string, string?> { ["hidden"] = null }).ToHtml());

    [Fact]
    public void A_verbatim_attribute_value_is_html_encoded() =>
        Assert.Equal(
            "<div data-x=\"&quot;&amp;\"></div>",
            Div.Attributes(new Dictionary<string, string?> { ["data-x"] = "\"&" }).ToHtml());

}
