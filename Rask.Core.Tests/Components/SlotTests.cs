using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SlotTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<slot></slot>", new Slot(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Slot.Props("s", "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<slot id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"s\"></slot>",
            new Slot(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<slot>&lt;x&gt;</slot>", new Slot(null, "<x>").ToHtml());
}
