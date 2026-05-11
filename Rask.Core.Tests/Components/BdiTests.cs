using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BdiTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<bdi></bdi>", new Bdi(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Bdi.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<bdi id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></bdi>",
            new Bdi(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<bdi>&lt;x&gt;</bdi>", new Bdi(null, "<x>").ToHtml());
}
