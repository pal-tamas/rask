namespace Rask.Core.Tests.Components;

public class ColTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<col />", Col().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<col id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" span=\"3\" />",
            Col(3, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
