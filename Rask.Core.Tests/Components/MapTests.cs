using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MapTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<map></map>", new Map(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Map.Props("m", "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<map id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"m\"></map>",
            new Map(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<map>&lt;x&gt;</map>", new Map(null, "<x>").ToHtml());
}
