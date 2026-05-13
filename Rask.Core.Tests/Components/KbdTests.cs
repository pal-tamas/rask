using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class KbdTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<kbd></kbd>", new Kbd().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<kbd id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></kbd>",
            new Kbd { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<kbd>&lt;x&gt;</kbd>", new Kbd { Children = ["<x>"] }.ToHtml());
}
