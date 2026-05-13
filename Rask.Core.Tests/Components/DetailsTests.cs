using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DetailsTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<details></details>", new Details().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<details id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" open></details>",
            new Details { Open = true, Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<details>&lt;x&gt;</details>", new Details { Children = ["<x>"] }.ToHtml());
}
