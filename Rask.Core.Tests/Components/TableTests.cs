using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TableTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<table></table>", new Table(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Table.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<table id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></table>",
            new Table(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<table>&lt;x&gt;</table>", new Table(null, "<x>").ToHtml());
}
