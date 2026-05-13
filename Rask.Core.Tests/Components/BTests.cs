using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<b></b>", B().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<b id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></b>",
            B(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<b>&lt;x&gt;</b>", B()["<x>"].ToHtml());
}
