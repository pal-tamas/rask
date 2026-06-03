namespace Rask.Core.Tests.Components;

public class WbrTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<wbr />", Wbr().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<wbr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            Wbr("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
