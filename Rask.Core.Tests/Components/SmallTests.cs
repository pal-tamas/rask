using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SmallTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<small></small>", new Small(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Small.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<small id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></small>",
            new Small(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<small>&lt;x&gt;</small>", new Small(null, "<x>").ToHtml());
}
