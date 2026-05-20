namespace Rask.Core.Tests.Components;

public class BrTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<br />", Br().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<br id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            Br("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
