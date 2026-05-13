using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MainTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<main></main>", new Main().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<main id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></main>",
            new Main { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<main>&lt;x&gt;</main>", new Main { Children = ["<x>"] }.ToHtml());
}
