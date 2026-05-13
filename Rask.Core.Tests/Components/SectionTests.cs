using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SectionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<section></section>", new Section().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<section id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></section>",
            new Section { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<section>&lt;x&gt;</section>", new Section { Children = ["<x>"] }.ToHtml());
}
