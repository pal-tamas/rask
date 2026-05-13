using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class FigureTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<figure></figure>", new Figure().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<figure id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></figure>",
            new Figure { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<figure>&lt;x&gt;</figure>", new Figure { Children = ["<x>"] }.ToHtml());
}
