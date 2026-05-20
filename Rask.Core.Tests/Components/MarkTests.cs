namespace Rask.Core.Tests.Components;

public class MarkTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<mark></mark>", Mark().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<mark id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></mark>",
            Mark("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<mark>&lt;x&gt;</mark>", Mark()["<x>"].ToHtml());
}
