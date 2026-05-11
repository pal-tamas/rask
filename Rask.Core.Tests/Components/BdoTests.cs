using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BdoTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<bdo></bdo>", new Bdo(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Bdo.Props("rtl", "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<bdo id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" dir=\"rtl\"></bdo>",
            new Bdo(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<bdo>&lt;x&gt;</bdo>", new Bdo(null, "<x>").ToHtml());
}
