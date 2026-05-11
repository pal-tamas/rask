using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class PTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<p></p>", new P(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new P.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<p id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></p>",
            new P(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<p>&lt;x&gt;</p>", new P(null, "<x>").ToHtml());
}
