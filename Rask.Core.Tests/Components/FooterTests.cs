using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class FooterTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<footer></footer>", new Footer(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Footer.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<footer id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></footer>",
            new Footer(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<footer>&lt;x&gt;</footer>", new Footer(null, "<x>").ToHtml());
}
