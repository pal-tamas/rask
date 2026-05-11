using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class CaptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<caption></caption>", new Caption(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Caption.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<caption id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></caption>",
            new Caption(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<caption>&lt;x&gt;</caption>", new Caption(null, "<x>").ToHtml());
}
