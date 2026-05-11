using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class FigcaptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<figcaption></figcaption>", new Figcaption(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Figcaption.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<figcaption id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></figcaption>",
            new Figcaption(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<figcaption>&lt;x&gt;</figcaption>", new Figcaption(null, "<x>").ToHtml());
}
