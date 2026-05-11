using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class StrongTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<strong></strong>", new Strong(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Strong.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<strong id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></strong>",
            new Strong(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<strong>&lt;x&gt;</strong>", new Strong(null, "<x>").ToHtml());
}
