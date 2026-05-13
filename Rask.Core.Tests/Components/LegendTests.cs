using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class LegendTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<legend></legend>", new Legend().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<legend id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></legend>",
            new Legend { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<legend>&lt;x&gt;</legend>", new Legend { Children = ["<x>"] }.ToHtml());
}
