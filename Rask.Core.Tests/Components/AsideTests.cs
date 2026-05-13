using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class AsideTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<aside></aside>", new Aside().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<aside id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></aside>",
            new Aside { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<aside>&lt;x&gt;</aside>", new Aside { Children = ["<x>"] }.ToHtml());
}
