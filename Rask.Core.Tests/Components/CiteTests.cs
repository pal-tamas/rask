using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class CiteTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<cite></cite>", new Cite().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<cite id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></cite>",
            new Cite { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<cite>&lt;x&gt;</cite>", new Cite { Children = ["<x>"] }.ToHtml());
}
