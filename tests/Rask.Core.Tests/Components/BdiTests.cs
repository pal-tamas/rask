namespace Rask.Core.Tests.Components;

public class BdiTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<bdi></bdi>", Bdi().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<bdi id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></bdi>",
            Bdi("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<bdi>&lt;x&gt;</bdi>", Bdi()["<x>"].ToHtml());
}
