namespace Rask.Core.Tests.Components;

public partial class CanvasTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<canvas></canvas>", Canvas.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<canvas id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" width=\"300\" height=\"150\"></canvas>",
            Canvas
                .Width(300)
                .Height(150)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<canvas>&lt;x&gt;</canvas>", Canvas["<x>"].ToHtml());
}
