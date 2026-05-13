using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class RtTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<rt></rt>", new Rt().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<rt id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></rt>",
            new Rt { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<rt>&lt;x&gt;</rt>", new Rt { Children = ["<x>"] }.ToHtml());
}
