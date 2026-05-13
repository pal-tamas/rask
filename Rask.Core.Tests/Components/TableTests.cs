using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TableTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<table></table>", Table().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<table id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></table>",
            Table(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<table>&lt;x&gt;</table>", Table()["<x>"].ToHtml());
}
