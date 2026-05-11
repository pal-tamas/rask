using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class AbbrTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<abbr></abbr>", new Abbr(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Abbr.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<abbr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></abbr>",
            new Abbr(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<abbr>&lt;x&gt;</abbr>", new Abbr(null, "<x>").ToHtml());
}
