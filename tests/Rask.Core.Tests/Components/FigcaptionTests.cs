namespace Rask.Core.Tests.Components;

public class FigcaptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<figcaption></figcaption>", Figcaption().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<figcaption id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></figcaption>",
            Figcaption("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<figcaption>&lt;x&gt;</figcaption>", Figcaption()["<x>"].ToHtml());
}
