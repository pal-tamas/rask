namespace Rask.Core.Tests.Components;

public partial class KbdTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<kbd></kbd>", Kbd.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<kbd id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></kbd>",
            Kbd.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<kbd>&lt;x&gt;</kbd>", Kbd["<x>"].ToHtml());
}
