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
                Assert.Equal("<wbr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            new Wbr { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }
}
