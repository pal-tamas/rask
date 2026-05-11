using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SpanTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<span></span>", new Span(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Span.Props(
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<span id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></span>",
            new Span(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<span>&lt;x&gt;</span>", new Span(null, "<x>").ToHtml());
}
