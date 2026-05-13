using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class RubyTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ruby></ruby>", new Ruby().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<ruby id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></ruby>",
            new Ruby { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ruby>&lt;x&gt;</ruby>", new Ruby { Children = ["<x>"] }.ToHtml());
}
