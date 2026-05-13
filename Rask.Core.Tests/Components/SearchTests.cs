using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SearchTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<search></search>", new Search().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<search id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></search>",
            new Search { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<search>&lt;x&gt;</search>", new Search { Children = ["<x>"] }.ToHtml());
}
