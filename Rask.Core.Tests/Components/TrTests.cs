using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TrTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<tr></tr>", new Tr(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Tr.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<tr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tr>",
            new Tr(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<tr>&lt;x&gt;</tr>", new Tr(null, "<x>").ToHtml());
}
