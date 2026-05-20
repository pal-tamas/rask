namespace Rask.Core.Tests.Components;

public class FooterTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<footer></footer>", Footer().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<footer id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></footer>",
            Footer("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<footer>&lt;x&gt;</footer>", Footer()["<x>"].ToHtml());
}
