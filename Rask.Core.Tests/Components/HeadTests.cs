using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HeadTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<head></head>", new Head(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Head.Props(
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<head id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></head>",
            new Head(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<head>&lt;x&gt;</head>", new Head(null, "<x>").ToHtml());
}
