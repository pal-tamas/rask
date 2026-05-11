using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<b></b>", new B(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new B.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<b id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></b>",
            new B(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<b>&lt;x&gt;</b>", new B(null, "<x>").ToHtml());
}
