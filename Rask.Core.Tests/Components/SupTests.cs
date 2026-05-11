using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<sup></sup>", new Sup(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Sup.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<sup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></sup>",
            new Sup(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<sup>&lt;x&gt;</sup>", new Sup(null, "<x>").ToHtml());
}
