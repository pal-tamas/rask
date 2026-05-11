using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ITests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<i></i>", new I(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new I.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<i id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></i>",
            new I(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<i>&lt;x&gt;</i>", new I(null, "<x>").ToHtml());
}
