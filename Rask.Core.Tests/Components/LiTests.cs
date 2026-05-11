using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class LiTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<li></li>", new Li(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Li.Props(42, "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<li id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"42\"></li>",
            new Li(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<li>&lt;x&gt;</li>", new Li(null, "<x>").ToHtml());
}
