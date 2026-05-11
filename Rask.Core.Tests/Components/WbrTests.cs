using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class WbrTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<wbr />", new Wbr().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Wbr.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<wbr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            new Wbr(props).ToHtml());
    }
}
