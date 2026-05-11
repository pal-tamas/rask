using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class RpTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rp></rp>", new Rp(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Rp.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<rp id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></rp>",
            new Rp(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<rp>&lt;x&gt;</rp>", new Rp(null, "<x>").ToHtml());
}
