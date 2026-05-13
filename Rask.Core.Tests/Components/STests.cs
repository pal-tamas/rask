using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class STests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<s></s>", new S().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<s id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></s>",
            new S { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<s>&lt;x&gt;</s>", new S { Children = ["<x>"] }.ToHtml());
}
