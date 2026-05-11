using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class FigureTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<figure></figure>", new Figure(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Figure.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<figure id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></figure>",
            new Figure(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<figure>&lt;x&gt;</figure>", new Figure(null, "<x>").ToHtml());
}
