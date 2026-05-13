using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TdTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<td></td>", new Td().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<td id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" colspan=\"2\" rowspan=\"3\" headers=\"h1 h2\"></td>",
            new Td { Colspan = 2, Rowspan = 3, Headers = "h1 h2", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<td>&lt;x&gt;</td>", new Td { Children = ["<x>"] }.ToHtml());
}
