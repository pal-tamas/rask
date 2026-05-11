using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SubTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<sub></sub>", new Sub(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Sub.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<sub id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></sub>",
            new Sub(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<sub>&lt;x&gt;</sub>", new Sub(null, "<x>").ToHtml());
}
