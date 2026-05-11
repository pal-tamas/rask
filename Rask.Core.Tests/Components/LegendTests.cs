using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class LegendTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<legend></legend>", new Legend(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Legend.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<legend id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></legend>",
            new Legend(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<legend>&lt;x&gt;</legend>", new Legend(null, "<x>").ToHtml());
}
