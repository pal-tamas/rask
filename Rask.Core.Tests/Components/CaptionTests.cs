namespace Rask.Core.Tests.Components;

public class CaptionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<caption></caption>", Caption().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<caption id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></caption>",
            Caption("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<caption>&lt;x&gt;</caption>", Caption()["<x>"].ToHtml());
}
