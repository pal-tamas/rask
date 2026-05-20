namespace Rask.Core.Tests.Components;

public class KbdTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<kbd></kbd>", Kbd().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<kbd id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></kbd>",
            Kbd("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<kbd>&lt;x&gt;</kbd>", Kbd()["<x>"].ToHtml());
}
