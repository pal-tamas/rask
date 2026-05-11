using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SearchTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<search></search>", new Search(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Search.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<search id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></search>",
            new Search(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<search>&lt;x&gt;</search>", new Search(null, "<x>").ToHtml());
}
