using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class QTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<q></q>", new Q(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Q.Props("https://x", "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<q id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\"></q>",
            new Q(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<q>&lt;x&gt;</q>", new Q(null, "<x>").ToHtml());
}
