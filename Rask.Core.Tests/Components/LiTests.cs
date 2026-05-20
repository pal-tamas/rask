namespace Rask.Core.Tests.Components;

public class LiTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<li></li>", Li().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<li id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"42\"></li>",
            Li(42, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<li>&lt;x&gt;</li>", Li()["<x>"].ToHtml());
}
