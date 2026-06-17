namespace Rask.Core.Tests.Components;

public class DivTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<div></div>", Div().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></div>",
            Div(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() => Assert.Equal("<div>&lt;x&gt;</div>", Div()["<x>"].ToHtml());

    [Fact]
    public void Render_NoAccessibilityProps_EmitsNoAriaRoleOrTabindex() =>
        Assert.Equal("<div></div>", Div().ToHtml());

    [Fact]
    public void Render_Aria_EmitsAriaPrefixedAttributes() =>
        Assert.Equal(
            "<div aria-label=\"Close\" aria-expanded=\"true\"></div>",
            Div(Aria: new Dictionary<string, string?> { ["label"] = "Close", ["expanded"] = "true" }).ToHtml());

    [Fact]
    public void Render_AriaNullValue_EmitsBareAttribute() =>
        Assert.Equal(
            "<div aria-hidden></div>",
            Div(Aria: new Dictionary<string, string?> { ["hidden"] = null }).ToHtml());

    [Fact]
    public void Render_AriaValue_IsHtmlEncoded() =>
        Assert.Equal(
            "<div aria-label=\"a &amp; b\"></div>",
            Div(Aria: new Dictionary<string, string?> { ["label"] = "a & b" }).ToHtml());

    [Fact]
    public void Render_RoleAndTabIndex_EmitNativeAttributes() =>
        Assert.Equal(
            "<div role=\"dialog\" tabindex=\"-1\"></div>",
            Div(Role: "dialog", TabIndex: -1).ToHtml());

    [Fact]
    public void Render_AccessibilityProps_FollowDataInDocumentedOrder() =>
        Assert.Equal(
            "<div id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" role=\"dialog\" tabindex=\"0\" aria-label=\"L\"></div>",
            Div(
                Id: "i",
                Class: "c",
                Style: "s",
                Data: new Dictionary<string, string?> { ["k"] = "v" },
                Role: "dialog",
                TabIndex: 0,
                Aria: new Dictionary<string, string?> { ["label"] = "L" }).ToHtml());
}
