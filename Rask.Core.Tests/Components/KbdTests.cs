using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class KbdTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<kbd></kbd>", new Kbd(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Kbd.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<kbd id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></kbd>",
            new Kbd(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<kbd>&lt;x&gt;</kbd>", new Kbd(null, "<x>").ToHtml());
}
