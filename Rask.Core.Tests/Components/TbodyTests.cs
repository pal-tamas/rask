using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TbodyTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<tbody></tbody>", new Tbody(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Tbody.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<tbody id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tbody>",
            new Tbody(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<tbody>&lt;x&gt;</tbody>", new Tbody(null, "<x>").ToHtml());
}
