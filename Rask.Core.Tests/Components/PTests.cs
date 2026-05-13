using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class PTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<p></p>", new P().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<p id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></p>",
            new P { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<p>&lt;x&gt;</p>", new P { Children = ["<x>"] }.ToHtml());
}
