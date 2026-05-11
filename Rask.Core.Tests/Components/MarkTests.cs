using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MarkTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<mark></mark>", new Mark(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Mark.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<mark id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></mark>",
            new Mark(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<mark>&lt;x&gt;</mark>", new Mark(null, "<x>").ToHtml());
}
