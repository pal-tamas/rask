using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ThTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<th></th>", Th().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<th id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" colspan=\"2\" rowspan=\"3\" headers=\"h1\" scope=\"col\" abbr=\"name\"></th>",
            Th(Colspan: 2, Rowspan: 3, Headers: "h1", Scope: "col", Abbr: "name", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<th>&lt;x&gt;</th>", Th()["<x>"].ToHtml());
}
