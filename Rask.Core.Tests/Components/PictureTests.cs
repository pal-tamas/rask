using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class PictureTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<picture></picture>", new Picture().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<picture id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></picture>",
            new Picture { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<picture>&lt;x&gt;</picture>", new Picture { Children = ["<x>"] }.ToHtml());
}
