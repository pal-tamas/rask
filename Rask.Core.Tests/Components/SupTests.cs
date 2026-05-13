using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<sup></sup>", new Sup().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<sup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></sup>",
            new Sup { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<sup>&lt;x&gt;</sup>", new Sup { Children = ["<x>"] }.ToHtml());
}
