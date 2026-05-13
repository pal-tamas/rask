using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class CaptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<caption></caption>", new Caption().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<caption id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></caption>",
            new Caption { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<caption>&lt;x&gt;</caption>", new Caption { Children = ["<x>"] }.ToHtml());
}
