using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ThTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<th></th>", new Th(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Th.Props(2, 3, "h1",
            "col", "name",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<th id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" colspan=\"2\" rowspan=\"3\" headers=\"h1\" scope=\"col\" abbr=\"name\"></th>",
            new Th(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<th>&lt;x&gt;</th>", new Th(null, "<x>").ToHtml());
}
