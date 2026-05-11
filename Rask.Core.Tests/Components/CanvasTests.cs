using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class CanvasTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<canvas></canvas>", new Canvas(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Canvas.Props(300, 150,
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<canvas id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" width=\"300\" height=\"150\"></canvas>",
            new Canvas(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<canvas>&lt;x&gt;</canvas>", new Canvas(null, "<x>").ToHtml());
}
