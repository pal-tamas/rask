using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class NavTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<nav></nav>", new Nav().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<nav id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></nav>",
            new Nav { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<nav>&lt;x&gt;</nav>", new Nav { Children = ["<x>"] }.ToHtml());
}
