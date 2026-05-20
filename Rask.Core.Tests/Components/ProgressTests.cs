namespace Rask.Core.Tests.Components;

public class ProgressTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<progress></progress>", Progress().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<progress id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"50.5\" max=\"100\"></progress>",
            Progress(50.5, 100, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<progress>&lt;x&gt;</progress>", Progress()["<x>"].ToHtml());
}
