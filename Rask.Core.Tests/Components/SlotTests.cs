using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SlotTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<slot></slot>", new Slot().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<slot id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"s\"></slot>",
            new Slot { Name = "s", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<slot>&lt;x&gt;</slot>", new Slot { Children = ["<x>"] }.ToHtml());
}
