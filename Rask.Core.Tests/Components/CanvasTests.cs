namespace Rask.Core.Tests.Components;

public class CanvasTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<canvas></canvas>", Canvas().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<canvas id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" width=\"300\" height=\"150\"></canvas>",
            Canvas(300, 150, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<canvas>&lt;x&gt;</canvas>", Canvas()["<x>"].ToHtml());
}
