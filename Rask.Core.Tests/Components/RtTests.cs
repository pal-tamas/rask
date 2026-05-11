using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class RtTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rt></rt>", new Rt(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Rt.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<rt id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></rt>",
            new Rt(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<rt>&lt;x&gt;</rt>", new Rt(null, "<x>").ToHtml());
}
