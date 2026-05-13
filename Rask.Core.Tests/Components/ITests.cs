using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ITests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<i></i>", new I().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<i id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></i>",
            new I { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<i>&lt;x&gt;</i>", new I { Children = ["<x>"] }.ToHtml());
}
