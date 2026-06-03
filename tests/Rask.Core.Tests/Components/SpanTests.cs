namespace Rask.Core.Tests.Components;

public class SpanTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<span></span>", Span().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<span id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></span>",
            Span("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<span>&lt;x&gt;</span>", Span()["<x>"].ToHtml());
}
