using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class NavTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<nav></nav>", new Nav(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Nav.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<nav id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></nav>",
            new Nav(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<nav>&lt;x&gt;</nav>", new Nav(null, "<x>").ToHtml());
}
