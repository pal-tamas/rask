using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class StrongTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<strong></strong>", new Strong().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<strong id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></strong>",
            new Strong { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<strong>&lt;x&gt;</strong>", new Strong { Children = ["<x>"] }.ToHtml());
}
