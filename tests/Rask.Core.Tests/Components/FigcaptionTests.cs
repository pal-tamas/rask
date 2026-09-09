namespace Rask.Core.Tests.Components;

public partial class FigcaptionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<figcaption></figcaption>", Figcaption.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<figcaption id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></figcaption>",
            Figcaption.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<figcaption>&lt;x&gt;</figcaption>", Figcaption["<x>"].ToHtml());
}
