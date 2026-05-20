namespace Rask.Core.Tests.Components;

public class CiteTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<cite></cite>", Cite().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<cite id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></cite>",
            Cite("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<cite>&lt;x&gt;</cite>", Cite()["<x>"].ToHtml());
}
