using Rask.Core;

namespace Rask.Core.Tests.Components;

// MIGRATED to the builder surface. A test class is not a component, so the entries do not reach it by
// the rule that reaches a component — `: RaskMarkup` is what puts them in scope, and it is the whole
// of the change besides rewriting each call as a chain.
public partial class DivTests : RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<div></div>", Div.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></div>",
            Div.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() => Assert.Equal("<div>&lt;x&gt;</div>", Div["<x>"].ToHtml());

    [Fact]
    public void Render_NoAccessibilityProps_EmitsNoAriaRoleOrTabindex() =>
        Assert.Equal("<div></div>", Div.ToHtml());

    [Fact]
    public void Render_Aria_EmitsAriaPrefixedAttributes() =>
        Assert.Equal(
            "<div aria-label=\"Close\" aria-expanded=\"true\"></div>",
            Div.Aria(new Dictionary<string, string?> { ["label"] = "Close", ["expanded"] = "true" }).ToHtml());

    [Fact]
    public void Render_AriaNullValue_EmitsBareAttribute() =>
        Assert.Equal(
            "<div aria-hidden></div>",
            Div.Aria(new Dictionary<string, string?> { ["hidden"] = null }).ToHtml());

    [Fact]
    public void Render_AriaValue_IsHtmlEncoded() =>
        Assert.Equal(
            "<div aria-label=\"a &amp; b\"></div>",
            Div.Aria(new Dictionary<string, string?> { ["label"] = "a & b" }).ToHtml());

    [Fact]
    public void Render_RoleAndTabIndex_EmitNativeAttributes() =>
        Assert.Equal(
            "<div role=\"dialog\" tabindex=\"-1\"></div>",
            Div.Role("dialog").TabIndex(-1).ToHtml());

    [Fact]
    public void Render_AccessibilityProps_FollowDataInDocumentedOrder() =>
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
    public void Render_Title_EmitsTheGlobalTooltipAttribute() =>
        Assert.Equal("<div title=\"2026-01-01 12:00:00Z\"></div>", Div.Title("2026-01-01 12:00:00Z").ToHtml());

    [Fact]
    public void Render_Title_IsEncoded() =>
        Assert.Equal("<div title=\"a &amp; &lt;b&gt;\"></div>", Div.Title("a & <b>").ToHtml());

    [Fact]
    // Title joins the plain global attributes after style, ahead of the prefixed data-*/aria-* groups.
    public void Render_Title_SitsAfterStyleAndBeforeData() =>
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
    public void Render_TitleUnset_EmitsNothing() => Assert.Equal("<div></div>", Div.ToHtml());
    // The escape hatch (#693). Emits last in the universal block, after aria-*, before tag-specific.
    [Fact]
    public void Render_Attributes_EmitAfterTheAriaGroup() =>
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
    public void Render_LangOnAPhrase_IsExpressible() =>
        Assert.Equal(
            // non-ASCII text is encoded by HtmlSerializer's safe-ASCII rule, hence the entities
            "<div lang=\"fr\">d&#xE9;j&#xE0; vu</div>",
            Div.Attributes(new Dictionary<string, string?> { ["lang"] = "fr" })["déjà vu"].ToHtml());

    [Fact]
    public void Render_NullValuedAttribute_EmitsItBare() =>
        Assert.Equal(
            "<div hidden></div>",
            Div.Attributes(new Dictionary<string, string?> { ["hidden"] = null }).ToHtml());

    [Fact]
    public void Render_AttributeValue_IsHtmlEncoded() =>
        Assert.Equal(
            "<div data-x=\"&quot;&amp;\"></div>",
            Div.Attributes(new Dictionary<string, string?> { ["data-x"] = "\"&" }).ToHtml());

}
